using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    public class GameTcpServer
    {
        private readonly TcpListener _listener;
        private readonly PlayerAuthValidator _authValidator;
        private readonly MainServerInternalApi _mainServerApi;
        private readonly KillRewardSaver _killRewardSaver;
        private readonly DisconnectedPlayerStateStore _disconnectedStates = new();
        private readonly SessionRegistry _sessions = new();
        private readonly X509Certificate2 _serverCertificate;

        // 서버 종료 시 남은 처치 보상 저장을 기다려 주는 최대 시간(docker stop 기본 유예 10초보다 짧게).
        private static readonly TimeSpan ShutdownSaveTimeout = TimeSpan.FromSeconds(8);

        public GameTcpServer(int port, IConfiguration configuration)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _authValidator = new PlayerAuthValidator(configuration);
            _mainServerApi = new MainServerInternalApi(configuration);
            _killRewardSaver = new KillRewardSaver(_mainServerApi);
            // 접속마다 새로 로드하지 않도록 서버 수명 동안 한 번만 준비해 모든 ClientSession이 공유한다.
            _serverCertificate = GameServerCertificateProvider.Load(configuration);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            // 몬스터 리스폰 타이머(GameRoom)가 개별 요청이 아니라 서버 전체 수명에 묶이도록,
            // 여기서 받은 ct를 MapRoomRegistry에 넘겨 앞으로 생성될 모든 GameRoom이 공유하게 한다.
            var mapRooms = new MapRoomRegistry(ct);

            _listener.Start();
            Console.WriteLine($"[GameServer] TCP 리스너 시작 (Port: {((IPEndPoint)_listener.LocalEndpoint).Port})");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(ct);
                    var session = new ClientSession(tcpClient, mapRooms, _authValidator, _mainServerApi, _killRewardSaver, _disconnectedStates, _sessions, _serverCertificate);
                    _ = session.RunAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 서버 종료로 인한 정상 취소
            }
            finally
            {
                _listener.Stop();

                // 종료 직전 처치의 보상이 아직 저장 중일 수 있다 - 프로세스가 끝나기 전에 마저 보낸다.
                await _killRewardSaver.DrainAsync(ShutdownSaveTimeout);
            }
        }
    }
}
