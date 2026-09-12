using System.Net.Sockets;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 접속 클라이언트 1개를 담당: 프레임 수신 루프 + OpCode 디스패치
    public class ClientSession
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly GameRoom _room;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        // 이 세션이 Game_EnterRequest로 등록한 플레이어 id. 등록 전이면 null.
        private long? _playerId;

        public ClientSession(TcpClient tcpClient, GameRoom room)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _room = room;
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
                if (_playerId is { } playerId)
                {
                    _room.Remove(playerId);
                    var left = new S2CPlayerLeft { PlayerId = playerId };
                    await _room.BroadcastAsync(OpCode.Game_PlayerLeft, left.Encode(), playerId, ct);
                }

                Console.WriteLine($"[GameServer] 클라이언트 종료: {endpoint}");
                _tcpClient.Close();
            }
        }

        private Task DispatchAsync(ushort opCode, byte[] body, CancellationToken ct)
        {
            return (OpCode)opCode switch
            {
                OpCode.System_Heartbeat => SendAsync(OpCode.System_Heartbeat, Array.Empty<byte>(), ct),
                OpCode.Game_EnterRequest => HandleEnterRequestAsync(body, ct),
                OpCode.Game_MoveRequest => HandleMoveRequestAsync(body, ct),
                _ => LogUnhandledAsync(opCode)
            };
        }

        private Task LogUnhandledAsync(ushort opCode)
        {
            Console.WriteLine($"[GameServer] 처리되지 않은 OpCode: 0x{opCode:X4}");
            return Task.CompletedTask;
        }

        // 룸에 자신을 등록하고, 본인에게는 기존 접속자 목록(Game_EnterAck)을, 나머지에게는 자신의 입장(Game_PlayerJoined)을 알린다.
        private async Task HandleEnterRequestAsync(byte[] body, CancellationToken ct)
        {
            var info = PlayerInfo.Decode(body);
            _playerId = info.PlayerId;

            var existingPlayers = _room.SnapshotExcluding(info.PlayerId);
            _room.Add(info, this);

            var ack = new S2CEnterAck { ExistingPlayers = existingPlayers };
            await SendAsync(OpCode.Game_EnterAck, ack.Encode(), ct);

            var joined = new S2CPlayerJoined { Player = info };
            await _room.BroadcastAsync(OpCode.Game_PlayerJoined, joined.Encode(), info.PlayerId, ct);
        }

        // 룸의 위치를 갱신하고, 본인을 제외한 나머지 접속자에게 브로드캐스트한다.
        private async Task HandleMoveRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = C2SMoveRequest.Decode(body);
            _room.UpdatePosition(request.PlayerId, request.X, request.Y, request.Z, request.RotationY);

            var broadcast = new S2CMoveBroadcast
            {
                PlayerId = request.PlayerId,
                X = request.X,
                Y = request.Y,
                Z = request.Z,
                RotationY = request.RotationY,
                Timestamp = request.Timestamp
            };

            await _room.BroadcastAsync(OpCode.Game_MoveBroadcast, broadcast.Encode(), request.PlayerId, ct);
        }

        // 여러 세션이 동시에(다른 플레이어의 브로드캐스트로) 같은 스트림에 쓸 수 있으므로 직렬화한다.
        public async Task SendAsync(OpCode opCode, byte[] body, CancellationToken ct)
        {
            var frame = PacketFrame.Encode((ushort)opCode, body);

            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(frame, ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
