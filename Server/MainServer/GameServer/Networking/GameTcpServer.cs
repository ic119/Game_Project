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
        private readonly X509Certificate2 _serverCertificate;

        public GameTcpServer(int port, IConfiguration configuration)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _authValidator = new PlayerAuthValidator(configuration);
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
                    var session = new ClientSession(tcpClient, mapRooms, _authValidator, _serverCertificate);
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
            }
        }
    }
}
