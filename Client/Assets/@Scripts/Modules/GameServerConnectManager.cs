using Incheol.Modules.Networking;
using Incheol.Utils;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
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
        private NetworkStream stream;
        private CancellationTokenSource cts;
        private readonly ConcurrentQueue<Action> pendingActions = new();

        private long localPlayerId;

        // ReadLoopAsync(백그라운드 스레드)와 Update/Disconnect(메인 스레드) 양쪽에서 읽고 쓰므로
        // volatile로 가시성을 보장한다.
        private volatile bool isConnected;
        private float heartbeatSendTimer;
        private float timeSinceLastHeartbeatAck;

        // 자발적으로 Disconnect()를 호출한 경우(씬 전환 등) OnDisconnected를 쓰지 않기 위한 구분값.
        private bool intentionalDisconnect;

        public event Action<GamePlayerInfo> OnPlayerJoined;
        public event Action<long> OnPlayerLeft;
        public event Action<GameMoveBroadcastPacket> OnPlayerMoved;
        public event Action<GameChatBroadcastPacket> OnChatReceived;
        public event Action<GameDamageBroadcastPacket> OnDamageReceived;
        public event Action<GameMonsterInfo> OnMonsterSpawned;
        public event Action<GameMonsterDamageBroadcastPacket> OnMonsterDamaged;
        public event Action<GameMonsterDieBroadcastPacket> OnMonsterDied;
        public event Action OnDisconnected;
        public event Action<string> OnServerError;

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
                action();
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

        #region Method
        /// <summary>
        /// GameServer에 접속하고 자신의 캐릭터 정보를 Game_EnterRequest로 전송한다.
        /// 접속 실패 시 DebugLogManager로 에러를 남기고 조용히 리턴한다(멀티 입장은 부가 기능이므로
        /// 로그인/게임 진행 자체를 막지 않는다).
        /// </summary>
        public void ConnectAndEnter(GamePlayerInfo localInfo)
        {
            _ = ConnectAndEnterAsync(localInfo);
        }

        private async Awaitable ConnectAndEnterAsync(GamePlayerInfo localInfo)
        {
            if (isConnected)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("이미 GameServer에 접속되어 있습니다.");
                return;
            }

            string accessToken = ServerConnectManager.Instance != null ? ServerConnectManager.Instance.AccessToken : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("로그인 세션이 없어 GameServer에 접속할 수 없습니다.");
                return;
            }

            try
            {
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                stream = tcpClient.GetStream();
                cts = new CancellationTokenSource();
                localPlayerId = localInfo.PlayerId;
                isConnected = true;
                intentionalDisconnect = false;
                heartbeatSendTimer = 0f;
                timeSinceLastHeartbeatAck = 0f;

                _ = ReadLoopAsync(cts.Token);

                // AccessToken은 본인 인증에만 쓰이므로 다른 플레이어에게도 브로드캐스트되는 GamePlayerInfo가 아니라
                // Game_EnterRequest 전용 래퍼(GameEnterRequestPacket)에만 담아 보낸다.
                var enterRequest = new GameEnterRequestPacket { AccessToken = accessToken, Player = localInfo };
                await SendAsync(GameOpCode.Game_EnterRequest, enterRequest.Encode());
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 접속 실패 : {exception.Message}");
                isConnected = false;
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

        public void Disconnect()
        {
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

            try
            {
                byte[] frame = GamePacketFrame.Encode((ushort)opCode, body);
                await stream.WriteAsync(frame, cts.Token);
            }
            catch (Exception exception)
            {
                // 접속이 끊긴 상태에서의 전송 실패는 ReadLoopAsync 쪽에서 이미 처리하지만, 디버깅을 위해 로그는 남겨둔다.
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 전송 실패 opCode={opCode} : {exception.Message}");
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await GamePacketFrame.ReadFrameAsync(stream, ct);
                    if (frame is null)
                    {
                        break;
                    }

                    HandleFrame(frame.Value.OpCode, frame.Value.Body);
                }
            }
            catch (Exception)
            {
                // 서버 종료/네트워크 단절 - 정상적인 종료 경로로 취급한다.
            }
            finally
            {
                isConnected = false;

                if (!intentionalDisconnect)
                {
                    pendingActions.Enqueue(() => OnDisconnected?.Invoke());
                }
            }
        }

        private void HandleFrame(ushort opCode, byte[] body)
        {
            switch ((GameOpCode)opCode)
            {
                case GameOpCode.Game_EnterAck:
                    var ack = GameEnterAckPacket.Decode(body);
                    foreach (GamePlayerInfo player in ack.ExistingPlayers)
                    {
                        pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(player));
                    }
                    foreach (GameMonsterInfo monster in ack.ExistingMonsters)
                    {
                        pendingActions.Enqueue(() => OnMonsterSpawned?.Invoke(monster));
                    }
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

                case GameOpCode.Game_MoveBroadcast:
                    var move = GameMoveBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerMoved?.Invoke(move));
                    break;

                case GameOpCode.Game_ChatBroadcast:
                    var chat = GameChatBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChatReceived?.Invoke(chat));
                    break;

                case GameOpCode.Game_DamageBroadcast:
                    var damage = GameDamageBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnDamageReceived?.Invoke(damage));
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

                // 클라이언트가 주기적으로 보낸 System_Heartbeat에 대한 서버 응답이다 - 타임아웃 타이머를 초기화한다.
                case GameOpCode.System_Heartbeat:
                    pendingActions.Enqueue(() => timeSinceLastHeartbeatAck = 0f);
                    break;

                // 서버가 Game_EnterRequest 인증 실패 등으로 연결을 끊기 직전에 보낸다(현재는 인증 실패 사유뿐).
                case GameOpCode.System_Error:
                    string errorMessage = Encoding.UTF8.GetString(body);
                    pendingActions.Enqueue(() => OnServerError?.Invoke(errorMessage));
                    break;
            }
        }
        #endregion
    }
}
