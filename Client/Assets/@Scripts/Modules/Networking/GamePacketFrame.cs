using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/PacketFrame.cs와 바이트 단위로 동일해야 한다.
    // 프레임 규격: [Length 4B][OpCode 2B][Body], 모든 정수는 Little-Endian.
    public static class GamePacketFrame
    {
        private const int LengthFieldSize = 4;
        private const int OpCodeFieldSize = 2;

        // 한 프레임의 최대 크기(OpCode + Body). Length 필드는 uint라 이론상 4GB까지 값이 올 수 있는데,
        // 검증 없이 그 값으로 배열을 할당하면 서버가 보낸 손상된 프레임 하나로 메모리 고갈을 유발할 수 있다.
        // 서버 Shared/Networking/PacketFrame.cs와 동일한 상한을 쓴다.
        private const int MaxPayloadSize = 64 * 1024;

        public static byte[] Encode(ushort opCode, byte[] body)
        {
            int payloadLength = OpCodeFieldSize + body.Length;
            var frame = new byte[LengthFieldSize + payloadLength];

            BitConverter.GetBytes((uint)payloadLength).CopyTo(frame, 0);
            BitConverter.GetBytes(opCode).CopyTo(frame, LengthFieldSize);
            body.CopyTo(frame, LengthFieldSize + OpCodeFieldSize);

            return frame;
        }

        public static async Task<(ushort OpCode, byte[] Body)?> ReadFrameAsync(Stream stream, CancellationToken ct)
        {
            var lengthBuffer = new byte[LengthFieldSize];
            if (!await ReadExactAsync(stream, lengthBuffer, ct))
            {
                return null;
            }

            uint payloadLength = BitConverter.ToUInt32(lengthBuffer, 0);
            if (payloadLength > MaxPayloadSize)
            {
                return null;
            }

            var payloadBuffer = new byte[payloadLength];
            if (!await ReadExactAsync(stream, payloadBuffer, ct))
            {
                return null;
            }

            ushort opCode = BitConverter.ToUInt16(payloadBuffer, 0);
            var body = new byte[payloadBuffer.Length - OpCodeFieldSize];
            Array.Copy(payloadBuffer, OpCodeFieldSize, body, 0, body.Length);

            return (opCode, body);
        }

        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
