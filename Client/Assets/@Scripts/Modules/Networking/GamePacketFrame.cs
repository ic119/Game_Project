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
