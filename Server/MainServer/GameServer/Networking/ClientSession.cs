using System.Net.Sockets;
using MessagePack;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 접속 클라이언트 1개를 담당: 프레임 수신 루프 + OpCode 디스패치
    public class ClientSession
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;

        public ClientSession(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var endpoint = _tcpClient.Client.RemoteEndPoint;
            Console.WriteLine($"[GameServer] 클라이언트 접속: {endpoint}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await PacketFrame.ReadFrameAsync(_stream, ct);
                    if (frame is null)
                        break;

                    await DispatchAsync(frame.Value.OpCode, frame.Value.Body, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 서버 종료로 인한 정상 취소
            }
            catch (IOException)
            {
                // 클라이언트 비정상 종료 (연결 끊김)
            }
            finally
            {
                Console.WriteLine($"[GameServer] 클라이언트 종료: {endpoint}");
                _tcpClient.Close();
            }
        }

        private Task DispatchAsync(ushort opCode, byte[] body, CancellationToken ct)
        {
            return (OpCode)opCode switch
            {
                OpCode.System_Heartbeat => SendAsync(OpCode.System_Heartbeat, Array.Empty<byte>(), ct),
                OpCode.Game_MoveRequest => HandleMoveRequestAsync(body, ct),
                _ => LogUnhandledAsync(opCode)
            };
        }

        private Task LogUnhandledAsync(ushort opCode)
        {
            Console.WriteLine($"[GameServer] 처리되지 않은 OpCode: 0x{opCode:X4}");
            return Task.CompletedTask;
        }

        // 임시 처리: AOI/룸 시스템이 없으므로 요청을 보낸 클라이언트에게 그대로 브로드캐스트 형태로 응답한다.
        private Task HandleMoveRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = MessagePackSerializer.Deserialize<C2SMoveRequest>(body);

            var broadcast = new S2CMoveBroadcast
            {
                PlayerId = request.PlayerId,
                X = request.X,
                Y = request.Y,
                Timestamp = request.Timestamp
            };

            return SendAsync(OpCode.Game_MoveBroadcast, MessagePackSerializer.Serialize(broadcast), ct);
        }

        private async Task SendAsync(OpCode opCode, byte[] body, CancellationToken ct)
        {
            var frame = PacketFrame.Encode((ushort)opCode, body);
            await _stream.WriteAsync(frame, ct);
        }
    }
}
