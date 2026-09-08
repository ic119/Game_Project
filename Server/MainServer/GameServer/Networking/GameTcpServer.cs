using System.Net;
using System.Net.Sockets;

namespace GameServer.Networking
{
    public class GameTcpServer
    {
        private readonly TcpListener _listener;

        public GameTcpServer(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
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
                    var session = new ClientSession(tcpClient);
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
