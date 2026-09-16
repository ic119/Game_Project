using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    public class GameTcpServer
    {
        private readonly TcpListener _listener;
        private readonly MapRoomRegistry _mapRooms = new();
        private readonly PlayerAuthValidator _authValidator;

        public GameTcpServer(int port, IConfiguration configuration)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _authValidator = new PlayerAuthValidator(configuration);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            _listener.Start();
            Console.WriteLine($"[GameServer] TCP 리스너 시작 (Port: {((IPEndPoint)_listener.LocalEndpoint).Port})");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(ct);
                    var session = new ClientSession(tcpClient, _mapRooms, _authValidator);
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
