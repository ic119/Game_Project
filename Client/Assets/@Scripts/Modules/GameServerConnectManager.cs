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
    public partial class GameServerConnectManager : SingletonObject<GameServerConnectManager>
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

    }
}
