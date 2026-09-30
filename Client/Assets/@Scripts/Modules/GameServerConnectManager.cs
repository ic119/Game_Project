using Incheol.Modules.Networking;
using Incheol.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// GameServer(TCP)에 접속해 자신의 캐릭터를 입장시키고, 다른 접속자의 입장/퇴장/이동을 이벤트로 알린다.
    /// ServerConnectManager(HTTP, Auth 서버)와는 별개의 접속으로, GameScene에 있는 동안만 연결을 유지한다.
    /// 수신 루프는 백그라운드 스레드에서 돌기 때문에 Unity API를 직접 건드릴 수 없다 - 디코딩된 이벤트는
    /// _pendingActions 큐에 넣고 Update()에서 메인 스레드로 드레인해서 호출한다.
    /// </summary>
    public class GameServerConnectManager : SingletonObject<GameServerConnectManager>
    {
        [Header("Game 서버(TCP) 접속 설정")]
        [SerializeField] private string host = "localhost";
        [SerializeField] private int port = 9000;

        [Header("하트비트(연결 생존 확인)")]
        [SerializeField, Min(1f)] private float heartbeatInterval = 10f;
        [SerializeField, Min(1f)] private float heartbeatTimeoutSeconds = 30f;

        protected override bool PersistAcrossScenes => true;

        private TcpClient tcpClient;

        // TLS 핸드셰이크가 끝나면 SslStream으로 교체된다(ConnectAndEnterAsync 참고). 이후 코드는 Stream
        // 인터페이스만 보므로 평문/암호화 여부를 신경 쓰지 않는다.
        private Stream stream;
        private CancellationTokenSource cts;
        private readonly ConcurrentQueue<Action> pendingActions = new();

        // stream 쓰기를 한 번에 하나로 직렬화한다(SendAsync 참고).
        private readonly SemaphoreSlim writeLock = new(1, 1);

        private long localPlayerId;

        // ReadLoopAsync(백그라운드 스레드)와 Update/Disconnect(메인 스레드) 양쪽에서 읽고 쓰므로
        // volatile로 가시성을 보장한다.
        private volatile bool isConnected;
        private float heartbeatSendTimer;
        private float timeSinceLastHeartbeatAck;

        // 자발적으로 Disconnect()를 호출한 경우(씬 전환 등) OnDisconnected를 쓰지 않기 위한 구분값.
        private bool intentionalDisconnect;

        // 서버가 System_Kicked(중복 접속 등)로 끊은 경우. 이때는 OnKicked로 사유를 이미 알렸으므로, 뒤이은 연결 종료에서
        // 재접속하거나 OnDisconnected("연결이 끊어졌습니다")를 또 발화하지 않는다. ReadLoopAsync(백그라운드)에서 쓰고 finally에서 읽는다.
        private volatile bool wasKicked;

        // 연결을 새로 맺거나 Disconnect()할 때마다 1씩 늘린다. 이전 연결의 수신 루프가 늦게 끝나면서 새 연결을 닫거나
        // 재접속을 또 시작하지 않도록, 각 연결은 자기 번호가 여전히 최신일 때만 정리/재접속을 진행한다.
        private int connectionGeneration;

        // 예기치 않게 끊겼을 때의 자동 재접속. 시도 사이 대기 시간이 곧 최대 시도 횟수다(약 23초 동안 5번).
        // 서버는 30초 동안 응답 없는 연결을 끊으므로, 짧은 네트워크 끊김이면 이 안에 다시 들어간다.
        private static readonly float[] ReconnectDelaysSeconds = { 1f, 2f, 4f, 8f, 8f };

        // 진행 중인 재접속 시도 번호(1부터). 0이면 재접속 중이 아니다. 메인 스레드에서만 읽고 쓴다.
        private int reconnectAttempt;

        // 재접속 때 다시 보낼 입장 정보(최초 ConnectAndEnter 값 + 맵 이동 시 MapId 갱신). 서버는 이 중 PlayerId/MapId만 쓰고
        // 나머지(위치/체력/스탯)는 DB와 맵 데이터로 다시 정한다.
        private GamePlayerInfo lastEnterInfo;

        // 이번 게임 입장(ConnectAndEnter)에서 한 번이라도 입장이 받아들여졌으면 true. 입장조차 못 한 연결(인증 실패 등)은
        // 재접속하지 않고 바로 OnDisconnected로 알린다. 메인 스레드에서만 읽고 쓴다.
        private bool hasEntered;

        public event Action<GamePlayerInfo> OnPlayerJoined;
        public event Action<long> OnPlayerLeft;
        /// <summary>
        /// 서버 방 틱(20Hz)마다 위치가 바뀐 원격 플레이어/몬스터 목록이 묶여 온다(Game_WorldSnapshot).
        /// </summary>
        public event Action<GameWorldSnapshotPacket> OnWorldSnapshot;

        /// <summary>
        /// 몬스터가 내 관심 영역(시야) 밖으로 나갔을 때 발생한다(Game_MonsterLeaveView). 시야에 들어올 때는 OnMonsterSpawned,
        /// 원격 플레이어의 시야 진입/이탈은 기존 OnPlayerJoined/OnPlayerLeft로 온다.
        /// </summary>
        public event Action<long> OnMonsterLeftView;
        public event Action<GameChatBroadcastPacket> OnChatReceived;
        public event Action<GameDamageBroadcastPacket> OnDamageReceived;

        /// <summary>
        /// 다른 플레이어가 공격 모션을 취했을 때 발생한다(Game_AttackAnimationBroadcast). 본인의 공격은
        /// 로컬에서 즉시 재생하므로 이 이벤트로 오지 않는다 - 원격 캐릭터 전용이다.
        /// </summary>
        public event Action<GameAttackAnimationBroadcastPacket> OnAttackAnimationReceived;

        /// <summary>
        /// 보물상자가 열렸을 때(Game_ChestOpenBroadcast) 발생한다. 본인이 방금 연 경우/다른 플레이어가 연 경우/
        /// 방에 새로 입장해 이미 열린 상자를 따라잡는 경우를 구분하지 않고 전부 이 이벤트로 온다 - 해당 ChestId를
        /// 가진 TreasureChestInteractionController가 알아서 자기 것인지 판단한다.
        /// </summary>
        public event Action<GameChestOpenBroadcastPacket> OnChestOpened;

        /// <summary>
        /// 방에 입장/맵 이동한 직후 서버가 알려주는 "지금 서 있는 상자 목록"(Game_ActiveChestsNotify). 고정 상자와
        /// 후보에서 뽑힌 상자가 함께 온다. 이미 열린 상자를 알리는 OnChestOpened보다 먼저 발생한다.
        /// </summary>
        public event Action<GameActiveChestsPacket> OnActiveChestsReceived;

        /// <summary>
        /// 리스폰으로 새 상자가 생겼을 때(Game_ChestSpawnBroadcast). 방에 있는 모두에게 온다.
        /// </summary>
        public event Action<GameChestInfo> OnChestSpawned;

        /// <summary>
        /// 열린 상자가 잔존 시간이 지나 사라졌을 때(Game_ChestDespawnBroadcast). 인자는 사라진 상자의 chestId다.
        /// </summary>
        public event Action<string> OnChestDespawned;

        /// <summary>
        /// 다른 플레이어(또는 본인)의 장착 장비가 바뀌었을 때(Game_EquipmentChangedBroadcast). 원격 캐릭터의 외형을 갱신하는 데 쓴다.
        /// </summary>
        public event Action<GameEquipmentChangedPacket> OnEquipmentChanged;
        public event Action<GameMonsterInfo> OnMonsterSpawned;
        public event Action<GameMonsterDamageBroadcastPacket> OnMonsterDamaged;
        public event Action<GameMonsterDieBroadcastPacket> OnMonsterDied;
        public event Action<GameMonsterAttackBroadcastPacket> OnMonsterAttacked;
        public event Action<GameExpGainBroadcastPacket> OnExpGained;
        public event Action<GameLootBroadcastPacket> OnLootReceived;
        public event Action<GamePlayerHpBroadcastPacket> OnPlayerHpChanged;
        public event Action<GamePlayerRevivedPacket> OnPlayerRevived;
        public event Action<GameUseItemResultPacket> OnUseItemResult;
        public event Action<GamePositionCorrectionPacket> OnPositionCorrected;

        /// <summary>
        /// 입장(최초/재접속)이 받아들여졌을 때(Game_EnterAck) 서버가 정한 본인 상태와 함께 발생한다. 같은 응답의
        /// 원격 플레이어/몬스터(OnPlayerJoined/OnMonsterSpawned)보다 먼저 온다.
        /// </summary>
        public event Action<GamePlayerInfo> OnEntered;

        /// <summary>
        /// 예기치 않게 끊겨 자동 재접속을 시도할 때마다 (시도 번호, 최대 횟수)와 함께 발생한다.
        /// </summary>
        public event Action<int, int> OnReconnecting;

        /// <summary>
        /// 자동 재접속에 성공해 다시 입장했을 때 발생한다(같은 응답의 OnEntered/OnPlayerJoined/OnMonsterSpawned 뒤).
        /// </summary>
        public event Action OnReconnected;

        /// <summary>
        /// 연결이 예기치 않게 끊겼고 자동 재접속도 모두 실패했을 때 발생한다.
        /// </summary>
        public event Action OnDisconnected;
        public event Action<string> OnServerError;

        /// <summary>
        /// 서버가 이 연결을 강제로 끊었을 때(같은 캐릭터로 다른 곳에서 접속 등) 사유 문자열과 함께 발생한다.
        /// 이 경우 OnDisconnected는 발생하지 않는다.
        /// </summary>
        public event Action<string> OnKicked;

        #region LifeCycle
        protected override void Awake()
        {
            base.Awake();

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // 릴리즈 빌드인데도 인스펙터 host 값이 기본값(localhost)이면 실서버 주소로 바꾸는 걸
            // 잊었을 가능성이 크다 - 조용히 로컬에 접속 시도하다 실패하는 대신 눈에 띄게 알린다.
            if (host == "localhost")
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("릴리즈 빌드인데 GameServer host가 기본값(localhost)입니다. 인스펙터에서 실제 서버 주소로 변경해야 합니다.");
            }
#endif
        }

        private void Update()
        {
            while (pendingActions.TryDequeue(out Action action))
            {
                // 이벤트 구독자 하나가 예외를 던져도 나머지 대기 이벤트와 하트비트 처리는 이번 프레임에 계속 진행한다.
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 이벤트 처리 중 오류 : {exception}");
                }
            }

            if (!isConnected)
            {
                return;
            }

            heartbeatSendTimer += Time.deltaTime;
            timeSinceLastHeartbeatAck += Time.deltaTime;

            if (timeSinceLastHeartbeatAck > heartbeatTimeoutSeconds)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"{heartbeatTimeoutSeconds}초 동안 GameServer 응답이 없어 연결을 끊습니다.");
                CloseConnectionUnexpectedly();
                return;
            }

            if (heartbeatSendTimer >= heartbeatInterval)
            {
                heartbeatSendTimer = 0f;
                _ = SendAsync(GameOpCode.System_Heartbeat, Array.Empty<byte>());
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Disconnect();
        }
        #endregion

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 로컬 개발 GameServer(GameServerCertificateProvider가 생성한 자체 서명 인증서)를 신뢰하기 위한 우회.
        /// ServerConnectManager.LocalDevCertificateHandler와 동일한 이유 - 실제 서버 인증서 검증을 완전히
        /// 생략하므로 UNITY_EDITOR/DEVELOPMENT_BUILD로 제한해 프로덕션 배포 빌드에는 절대 포함되지 않게 한다.
        /// </summary>
        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
#else
        // 프로덕션 빌드는 정상적인 인증서 검증(OS 신뢰 저장소 기준)을 그대로 따른다 - GameServer:TlsCertPath에
        // 실제 인증서가 설정되어 있어야 접속에 성공한다(GameServerCertificateProvider.cs 참고).
        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None;
        }
#endif

        #region Method
        /// <summary>
        /// GameServer에 접속하고 자신의 캐릭터 정보를 Game_EnterRequest로 전송한다.
        /// 접속 실패 시 DebugLogManager로 에러를 남기고 조용히 리턴한다(멀티 입장은 부가 기능이므로
        /// 로그인/게임 진행 자체를 막지 않는다).
        /// </summary>
        public void ConnectAndEnter(GamePlayerInfo localInfo)
        {
            lastEnterInfo = localInfo;
            reconnectAttempt = 0;
            hasEntered = false;
            _ = ConnectAndEnterAsync(localInfo, ++connectionGeneration);
        }

        // 접속과 입장 요청 전송까지 성공하면 true. 입장이 받아들여졌는지는 Game_EnterAck(OnEntered)로 안다.
        // 그 사이 Disconnect()나 새 접속으로 generation이 바뀌었으면 만든 연결을 닫고 false를 반환한다.
        private async Awaitable<bool> ConnectAndEnterAsync(GamePlayerInfo localInfo, int generation)
        {
            if (isConnected)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("이미 GameServer에 접속되어 있습니다.");
                return false;
            }

            // GameServer는 입장 시 이 토큰으로 MainServer에 캐릭터 소유권을 확인한다. 로비에 오래 머물렀으면 이미 만료됐을 수
            // 있어(30분) AccessToken 속성을 그대로 쓰지 않고, 만료가 임박하면 재발급한 토큰을 받는다.
            string accessToken = ServerConnectManager.Instance != null ? await ServerConnectManager.Instance.GetValidAccessTokenAsync() : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("로그인 세션이 없어 GameServer에 접속할 수 없습니다.");
                return false;
            }

            var newTcpClient = new TcpClient();
            try
            {
                await newTcpClient.ConnectAsync(host, port);

                // GameServer(ClientSession.RunAsync)가 접속을 받자마자 TLS 핸드셰이크부터 요구하므로,
                // 프레임을 하나라도 보내기 전에 SslStream으로 감싸고 인증을 마쳐야 한다.
                var sslStream = new SslStream(newTcpClient.GetStream(), leaveInnerStreamOpen: false, ValidateServerCertificate);
                await sslStream.AuthenticateAsClientAsync(host);

                if (generation != connectionGeneration)
                {
                    // 접속하는 사이 Disconnect()(씬 전환 등)가 호출됐다 - 이 연결은 쓰지 않는다.
                    sslStream.Close();
                    newTcpClient.Close();
                    return false;
                }

                tcpClient = newTcpClient;
                stream = sslStream;
                cts = new CancellationTokenSource();
                localPlayerId = localInfo.PlayerId;
                isConnected = true;
                intentionalDisconnect = false;
                wasKicked = false;
                ServerClock.Reset();
                heartbeatSendTimer = 0f;
                timeSinceLastHeartbeatAck = 0f;

                _ = ReadLoopAsync(sslStream, newTcpClient, generation, cts.Token);

                // AccessToken은 본인 인증에만 쓰이므로 다른 플레이어에게도 브로드캐스트되는 GamePlayerInfo가 아니라
                // Game_EnterRequest 전용 래퍼(GameEnterRequestPacket)에만 담아 보낸다.
                var enterRequest = new GameEnterRequestPacket { AccessToken = accessToken, Player = localInfo };
                await SendAsync(GameOpCode.Game_EnterRequest, enterRequest.Encode());
                return true;
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 접속 실패 : {exception.Message}");
                newTcpClient.Close();
                return false;
            }
        }

        /// <summary>
        /// 연결이 예기치 않게 끊겼을 때(메인 스레드) 다음 재접속을 예약한다. 시도를 다 썼거나, 이번 게임에서 한 번도 입장하지
        /// 못했으면(인증 실패 등 - 다시 시도해도 같다) OnDisconnected로 알린다.
        /// </summary>
        private void ScheduleReconnect()
        {
            if (lastEnterInfo == null || !hasEntered || reconnectAttempt >= ReconnectDelaysSeconds.Length)
            {
                reconnectAttempt = 0;
                OnDisconnected?.Invoke();
                return;
            }

            float delay = ReconnectDelaysSeconds[reconnectAttempt];
            reconnectAttempt++;
            OnReconnecting?.Invoke(reconnectAttempt, ReconnectDelaysSeconds.Length);
            _ = ReconnectAfterDelayAsync(delay, connectionGeneration);
        }

        private async Awaitable ReconnectAfterDelayAsync(float delaySeconds, int generation)
        {
            await Awaitable.WaitForSecondsAsync(delaySeconds);

            // 기다리는 사이 Disconnect()(씬 전환/로그아웃)가 호출됐으면 재접속하지 않는다.
            if (this == null || generation != connectionGeneration)
            {
                return;
            }

            DebugLogManager.GenerateLogMessage<GameServerConnectManager>($"GameServer 재접속 시도 ({reconnectAttempt}/{ReconnectDelaysSeconds.Length})");

            // 접속 자체가 실패하면 수신 루프가 없어 끊김 알림도 오지 않으므로 여기서 바로 다음 시도를 예약한다.
            // 접속은 됐지만 입장이 거부되면(서버가 System_Error 후 연결을 닫음) 수신 루프 종료가 다음 시도를 예약한다.
            if (!await ConnectAndEnterAsync(lastEnterInfo, ++connectionGeneration) && generation + 1 == connectionGeneration)
            {
                ScheduleReconnect();
            }
        }

        /// <summary>
        /// 자신의 현재 위치/회전을 GameServer에 보낸다(Game_MoveRequest). 접속 전이면 아무 것도 하지 않는다.
        /// </summary>
        public void SendMove(float x, float y, float z, float rotationY)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameMoveRequestPacket
            {
                PlayerId = localPlayerId,
                X = x,
                Y = y,
                Z = z,
                RotationY = rotationY,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_MoveRequest, request.Encode());
        }

        /// <summary>
        /// 채팅 메시지를 GameServer에 보낸다(Game_ChatRequest). 닉네임은 서버가 룸 등록 정보로 채우므로 보내지 않는다.
        /// 접속 전이거나 빈 문자열이면 아무 것도 하지 않는다.
        /// </summary>
        public void SendChat(string message)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var request = new GameChatRequestPacket
            {
                PlayerId = localPlayerId,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_ChatRequest, request.Encode());
        }

        /// <summary>
        /// 같은 접속을 유지한 채 다른 맵으로 이동했음을 GameServer에 알린다(Game_MapChangeRequest).
        /// 서버는 이전 맵 방에서 빼고 새 맵 방에 등록한 뒤, 새 맵의 기존 접속자 목록을 Game_MapChangeAck로 돌려준다.
        /// MapPortalController가 맵(프리팹) 교체를 마친 직후 호출해야 한다.
        /// </summary>
        public void SendMapChange(string mapId, float x, float y, float z, float rotationY)
        {
            if (!isConnected)
            {
                return;
            }

            // 재접속하면 지금 있는 맵으로 다시 입장해야 한다.
            if (lastEnterInfo != null)
            {
                lastEnterInfo.MapId = mapId;
            }

            var request = new GameMapChangeRequestPacket
            {
                PlayerId = localPlayerId,
                MapId = mapId,
                X = x,
                Y = y,
                Z = z,
                RotationY = rotationY
            };

            _ = SendAsync(GameOpCode.Game_MapChangeRequest, request.Encode());
        }

        /// <summary>
        /// 공격 의사를 GameServer에 보낸다(Game_AttackRequest). 데미지 수치는 보내지 않는다 - 서버가
        /// Game_EnterRequest 때 등록된 자신의 AttackPower를 사용해 그대로 중계하고, 방어력 차감은
        /// 각 클라이언트가 target의 로컬 Defense로 직접 계산한다.
        /// </summary>
        public void SendAttack(long targetId)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameAttackRequestPacket
            {
                AttackerId = localPlayerId,
                TargetId = targetId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_AttackRequest, request.Encode());
        }

        /// <summary>
        /// 공격 모션을 GameServer에 알린다(Game_AttackAnimationRequest). SendAttack/SendMonsterAttack과 달리
        /// 대상 유무와 무관하게 콤보 타수마다(허공 스윙 포함) 매번 호출해야 한다 - 근처 다른 플레이어가
        /// 내 스윙 모션 자체를 볼 수 있어야 하기 때문이다. 데미지 판정에는 전혀 쓰이지 않는 순수 연출용이다.
        /// weaponType은 현재 장착 무기(WeaponType enum 값)를 그대로 담아 보낸다 - 서버는 해석하지 않고
        /// 그대로 중계하며, 받는 쪽(RemoteCharacterController)이 이 값으로 무기별 애니메이션을 고른다.
        /// </summary>
        public void SendAttackAnimation(int comboStage, WeaponType weaponType)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameAttackAnimationRequestPacket
            {
                AttackerId = localPlayerId,
                ComboStage = comboStage,
                WeaponType = (int)weaponType
            };

            _ = SendAsync(GameOpCode.Game_AttackAnimationRequest, request.Encode());
        }

        /// <summary>
        /// 보물상자 개봉을 GameServer에 요청한다(Game_ChestOpenRequest). chestId는 MapData/{mapId}.json의
        /// chests[].id와 정확히 일치해야 한다(TreasureChestInteractionController.chestId, MapDataExporter가 내보낸 값).
        /// 결과는 즉시 돌아오지 않고 OnChestOpened(성공 시) 또는 아무 반응 없음(실패 시 - 이미 열렸거나 사거리 밖)으로 온다.
        /// </summary>
        public void SendChestOpenRequest(string chestId)
        {
            if (!isConnected || string.IsNullOrEmpty(chestId))
            {
                return;
            }

            var request = new GameChestOpenRequestPacket { ChestId = chestId };
            _ = SendAsync(GameOpCode.Game_ChestOpenRequest, request.Encode());
        }

        /// <summary>
        /// 몬스터에 대한 공격 의사를 GameServer에 보낸다(Game_MonsterAttackRequest). 플레이어 공격(SendAttack)과
        /// 달리 데미지 계산은 서버가 직접 수행한다 - 몬스터는 소유 클라이언트가 없어 로컬 Defense로 계산할
        /// 대상이 없기 때문이다. 결과는 OnMonsterDamaged(RemainingHp 포함)로 돌아온다.
        /// </summary>
        public void SendMonsterAttack(long monsterId)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameMonsterAttackRequestPacket
            {
                AttackerId = localPlayerId,
                MonsterId = monsterId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_MonsterAttackRequest, request.Encode());
        }

        /// <summary>
        /// 인벤토리에서 장비를 장착/해제해 바뀐 공격력/방어력을 GameServer에 알린다(Game_StatUpdateRequest).
        /// 서버는 이 값을 Game_EnterRequest 때와 같은 신뢰 수준으로 그대로 캐싱만 하고 응답하지 않는다.
        /// 접속 전이면(아직 GameScene 진입 전, 또는 이미 끊긴 상태) 아무 것도 하지 않는다 - 그 경우 최신 값은
        /// 다음 Game_EnterRequest(재접속)에 자연히 실려 간다.
        /// </summary>
        public void SendStatUpdate(int attackPower, int defense)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameStatUpdateRequestPacket
            {
                PlayerId = localPlayerId,
                AttackPower = attackPower,
                Defense = defense
            };

            _ = SendAsync(GameOpCode.Game_StatUpdateRequest, request.Encode());
        }

        /// <summary>
        /// 소비 아이템(물약 등) 사용을 GameServer에 요청한다(Game_UseItemRequest). 회복과 아이템 차감은 서버가 하고,
        /// 결과는 OnUseItemResult(차감 여부), 회복된 체력은 OnPlayerHpChanged로 돌아온다.
        /// 접속 전이면 보내지 않고 false를 반환한다 - 호출측이 응답을 기다리는 상태로 남지 않게 하기 위함이다.
        /// </summary>
        public bool SendUseItem(string itemId)
        {
            if (!isConnected)
            {
                return false;
            }

            var request = new GameUseItemRequestPacket { PlayerId = localPlayerId, ItemId = itemId };
            _ = SendAsync(GameOpCode.Game_UseItemRequest, request.Encode());
            return true;
        }

        public void Disconnect()
        {
            // 연결이 없어도(재접속 대기 중) 번호를 바꿔 예약된 재접속을 취소한다.
            connectionGeneration++;
            reconnectAttempt = 0;
            lastEnterInfo = null;

            if (!isConnected)
            {
                return;
            }

            intentionalDisconnect = true;
            isConnected = false;
            cts?.Cancel();
            stream?.Close();
            tcpClient?.Close();
        }

        // 하트비트 타임아웃 등 연결이 예기치 않게 끝났을 때 쓴다. Disconnect()와 달리
        // intentionalDisconnect를 설정하지 않아서 ReadLoopAsync의 finally가 OnDisconnected를 발화하게 된다.
        private void CloseConnectionUnexpectedly()
        {
            isConnected = false;
            cts?.Cancel();
            stream?.Close();
            tcpClient?.Close();
        }

        private async Task SendAsync(GameOpCode opCode, byte[] body)
        {
            if (stream == null)
            {
                return;
            }

            byte[] frame = GamePacketFrame.Encode((ushort)opCode, body);
            CancellationToken ct = cts.Token;
            bool lockTaken = false;

            try
            {
                // 이동(주기 전송)/하트비트/공격/채팅이 모두 fire-and-forget으로 이 메서드를 호출하므로, 앞선 쓰기가
                // 끝나기 전에 다음 쓰기가 시작될 수 있다. SslStream은 동시 쓰기를 지원하지 않아 프레임이 섞이거나
                // NotSupportedException이 나므로, 서버 ClientSession.SendAsync와 같은 방식으로 한 번에 하나씩 쓴다.
                await writeLock.WaitAsync(ct);
                lockTaken = true;
                await stream.WriteAsync(frame, ct);
            }
            catch (OperationCanceledException)
            {
                // Disconnect()로 연결을 정리하는 중 - 대기 중이던 전송은 조용히 버린다.
            }
            catch (Exception exception)
            {
                // 접속이 끊긴 상태에서의 전송 실패는 ReadLoopAsync 쪽에서 이미 처리하지만, 디버깅을 위해 로그는 남겨둔다.
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 전송 실패 opCode={opCode} : {exception.Message}");
            }
            finally
            {
                if (lockTaken)
                {
                    writeLock.Release();
                }
            }
        }

        // 연결 하나의 수신 루프. 그 연결의 스트림/소켓과 번호(generation)를 직접 받는다 - 필드(stream/tcpClient)는 재접속하면
        // 새 연결로 바뀌므로, 늦게 끝난 이전 루프가 필드를 닫으면 새 연결이 끊긴다.
        private async Task ReadLoopAsync(Stream connectionStream, TcpClient connectionClient, int generation, CancellationToken ct)
        {
            // 마지막으로 해석하던 프레임의 OpCode(오류 로그용).
            ushort lastOpCode = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await GamePacketFrame.ReadFrameAsync(connectionStream, ct);
                    if (frame is null)
                    {
                        break;
                    }

                    lastOpCode = frame.Value.OpCode;
                    HandleFrame(frame.Value.OpCode, frame.Value.Body);
                }
            }
            catch (Exception exception) when (exception is IOException and not EndOfStreamException
                                               || exception is ObjectDisposedException or OperationCanceledException)
            {
                // 서버 종료/네트워크 단절/직접 Disconnect() - 정상적인 종료 경로로 취급한다.
            }
            catch (Exception exception)
            {
                // 잘못된 프레임 길이(InvalidDataException), 형식이 맞지 않는 바디(EndOfStreamException 등). 서버와 클라이언트의
                // 패킷 정의가 어긋났을 가능성이 크다 - 예전에는 이것도 "정상 종료"로 삼켜 원인을 알 수 없었다.
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 수신 데이터 처리 실패로 연결을 끊습니다 (opCode=0x{lastOpCode:X4}) : {exception}");
            }
            finally
            {
                // 오류로 루프가 끝났을 때도 소켓을 확실히 닫는다(서버가 끊은 경우 다시 닫아도 무해하다).
                connectionStream.Close();
                connectionClient.Close();

                // 번호 확인은 메인 스레드에서 한다(connectionGeneration은 메인 스레드에서만 바뀐다). 그 사이 Disconnect()나
                // 새 연결로 번호가 바뀌었으면 이 연결은 이미 버려진 것이라 아무 것도 하지 않는다.
                bool endedUnexpectedly = !intentionalDisconnect && !wasKicked;
                pendingActions.Enqueue(() =>
                {
                    if (generation != connectionGeneration)
                    {
                        return;
                    }

                    isConnected = false;

                    if (endedUnexpectedly)
                    {
                        ScheduleReconnect();
                    }
                });
            }
        }

        private void HandleFrame(ushort opCode, byte[] body)
        {
            switch ((GameOpCode)opCode)
            {
                case GameOpCode.Game_EnterAck:
                    var ack = GameEnterAckPacket.Decode(body);
                    pendingActions.Enqueue(() =>
                    {
                        hasEntered = true;
                        OnEntered?.Invoke(ack.Self);
                    });
                    foreach (GamePlayerInfo player in ack.ExistingPlayers)
                    {
                        pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(player));
                    }
                    foreach (GameMonsterInfo monster in ack.ExistingMonsters)
                    {
                        pendingActions.Enqueue(() => OnMonsterSpawned?.Invoke(monster));
                    }
                    pendingActions.Enqueue(() =>
                    {
                        if (reconnectAttempt > 0)
                        {
                            reconnectAttempt = 0;
                            OnReconnected?.Invoke();
                        }
                    });
                    break;

                // 페이로드 구조가 Game_EnterAck과 동일하므로(새 맵의 기존 접속자/몬스터 목록) 같은 디코더를 재사용한다.
                // 이전 맵에서 스폰돼있던 원격 플레이어/몬스터 정리는 서버 응답을 기다리지 않고 맵 전환을 시작한
                // 클라이언트 쪽(RemotePlayerManager.ClearAll/RemoteMonsterManager.ClearAll)에서 이미 처리했다는 전제다.
                case GameOpCode.Game_MapChangeAck:
                    var mapChangeAck = GameEnterAckPacket.Decode(body);
                    foreach (GamePlayerInfo player in mapChangeAck.ExistingPlayers)
                    {
                        pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(player));
                    }
                    foreach (GameMonsterInfo monster in mapChangeAck.ExistingMonsters)
                    {
                        pendingActions.Enqueue(() => OnMonsterSpawned?.Invoke(monster));
                    }
                    break;

                case GameOpCode.Game_PlayerJoined:
                    var joined = GamePlayerJoinedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(joined.Player));
                    break;

                case GameOpCode.Game_PlayerLeft:
                    var left = GamePlayerLeftPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerLeft?.Invoke(left.PlayerId));
                    break;

                case GameOpCode.Game_WorldSnapshot:
                    var snapshot = GameWorldSnapshotPacket.Decode(body);
                    // 서버 시각 추정(ServerClock)은 메인 스레드에서만 다루므로 이벤트와 같은 액션 안에서 먼저 갱신한다 -
                    // 구독자(원격 개체 보간)가 이 스냅샷을 쓰는 시점에는 이미 반영돼 있다.
                    pendingActions.Enqueue(() =>
                    {
                        ServerClock.Observe(snapshot.ServerTimeMs);
                        OnWorldSnapshot?.Invoke(snapshot);
                    });
                    break;

                case GameOpCode.Game_MonsterLeaveView:
                    var monsterLeaveView = GameMonsterLeaveViewPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterLeftView?.Invoke(monsterLeaveView.MonsterId));
                    break;

                case GameOpCode.Game_ChatBroadcast:
                    var chat = GameChatBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChatReceived?.Invoke(chat));
                    break;

                case GameOpCode.Game_DamageBroadcast:
                    var damage = GameDamageBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnDamageReceived?.Invoke(damage));
                    break;

                case GameOpCode.Game_AttackAnimationBroadcast:
                    var attackAnimation = GameAttackAnimationBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnAttackAnimationReceived?.Invoke(attackAnimation));
                    break;

                case GameOpCode.Game_ChestOpenBroadcast:
                    var chestOpened = GameChestOpenBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChestOpened?.Invoke(chestOpened));
                    break;

                case GameOpCode.Game_ActiveChestsNotify:
                    var activeChests = GameActiveChestsPacket.Decode(body);
                    pendingActions.Enqueue(() => OnActiveChestsReceived?.Invoke(activeChests));
                    break;

                case GameOpCode.Game_ChestSpawnBroadcast:
                    var chestSpawn = GameChestSpawnPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChestSpawned?.Invoke(chestSpawn.Chest));
                    break;

                case GameOpCode.Game_EquipmentChangedBroadcast:
                    var equipmentChanged = GameEquipmentChangedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnEquipmentChanged?.Invoke(equipmentChanged));
                    break;

                case GameOpCode.Game_ChestDespawnBroadcast:
                    var chestDespawn = GameChestDespawnPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChestDespawned?.Invoke(chestDespawn.ChestId));
                    break;

                case GameOpCode.Game_MonsterSpawnBroadcast:
                    var monsterSpawn = GameMonsterSpawnBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterSpawned?.Invoke(monsterSpawn.Monster));
                    break;

                case GameOpCode.Game_MonsterDamageBroadcast:
                    var monsterDamage = GameMonsterDamageBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterDamaged?.Invoke(monsterDamage));
                    break;

                case GameOpCode.Game_MonsterDieBroadcast:
                    var monsterDie = GameMonsterDieBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterDied?.Invoke(monsterDie));
                    break;

                case GameOpCode.Game_MonsterAttackBroadcast:
                    var monsterAttack = GameMonsterAttackBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterAttacked?.Invoke(monsterAttack));
                    break;

                case GameOpCode.Game_ExpGainBroadcast:
                    var expGain = GameExpGainBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnExpGained?.Invoke(expGain));
                    break;

                case GameOpCode.Game_LootBroadcast:
                    var loot = GameLootBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnLootReceived?.Invoke(loot));
                    break;

                case GameOpCode.Game_PlayerHpBroadcast:
                    var hpChanged = GamePlayerHpBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerHpChanged?.Invoke(hpChanged));
                    break;

                case GameOpCode.Game_PlayerRevived:
                    var revived = GamePlayerRevivedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerRevived?.Invoke(revived));
                    break;

                case GameOpCode.Game_UseItemResult:
                    var useItemResult = GameUseItemResultPacket.Decode(body);
                    pendingActions.Enqueue(() => OnUseItemResult?.Invoke(useItemResult));
                    break;

                case GameOpCode.Game_PositionCorrection:
                    var positionCorrection = GamePositionCorrectionPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPositionCorrected?.Invoke(positionCorrection));
                    break;

                // 클라이언트가 주기적으로 보낸 System_Heartbeat에 대한 서버 응답이다 - 타임아웃 타이머를 초기화한다.
                case GameOpCode.System_Heartbeat:
                    pendingActions.Enqueue(() => timeSinceLastHeartbeatAck = 0f);
                    break;

                // 서버가 Game_EnterRequest 인증 실패 등으로 연결을 끊기 직전에 보낸다(현재는 인증 실패 사유뿐).
                case GameOpCode.System_Error:
                    string errorMessage = Encoding.UTF8.GetString(body);
                    pendingActions.Enqueue(() => OnServerError?.Invoke(errorMessage));
                    break;

                // 서버가 이 연결을 강제로 끊기 직전에 보낸다(현재는 같은 캐릭터 중복 접속). 곧 연결이 닫히지만
                // OnDisconnected 대신 이 사유만 알린다.
                case GameOpCode.System_Kicked:
                    wasKicked = true;
                    string kickReason = Encoding.UTF8.GetString(body);
                    pendingActions.Enqueue(() => OnKicked?.Invoke(kickReason));
                    break;
            }
        }
        #endregion
    }
}
