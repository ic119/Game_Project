using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    public class GameTcpServer
    {
        private readonly TcpListener _listener;
        private readonly PlayerAuthValidator _authValidator;

        public GameTcpServer(int port, IConfiguration configuration)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _authValidator = new PlayerAuthValidator(configuration);
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
                    var session = new ClientSession(tcpClient, mapRooms, _authValidator);
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
