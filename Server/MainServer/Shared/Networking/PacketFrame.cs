namespace Shared.Networking
{
    // 프레임 규격: [Length 4B][OpCode 2B][MessagePack Body]
    // - Length = OpCode(2B) + Body 길이의 합 (Length 필드 자신은 포함하지 않음)
    // - 모든 정수 필드는 Little-Endian으로 기록/해석한다.
    public static class PacketFrame
    {
        private const int LengthFieldSize = 4;
        private const int OpCodeFieldSize = 2;

        // 한 프레임의 최대 크기(OpCode + Body). Length 필드는 uint라 이론상 4GB까지 값이 올 수 있는데,
        // 검증 없이 그 값으로 배열을 할당하면 손상되거나 조작된 프레임 하나로 메모리 고갈을 유발할 수 있다.
        // 가장 큰 실사용 페이로드(채팅 200자 UTF-8 등)보다 넉넉히 잡은 상한이다.
        private const int MaxPayloadSize = 64 * 1024;

        public static byte[] Encode(ushort opCode, byte[] messagePackBody)
        {
            int payloadLength = OpCodeFieldSize + messagePackBody.Length;
            var frame = new byte[LengthFieldSize + payloadLength];

            BitConverter.GetBytes((uint)payloadLength).CopyTo(frame, 0);
            BitConverter.GetBytes(opCode).CopyTo(frame, LengthFieldSize);
            messagePackBody.CopyTo(frame, LengthFieldSize + OpCodeFieldSize);

            return frame;
        }

        // frameBytesWithoutLength: Length(4B)를 제외한 [OpCode 2B][Body] 구간
        public static (ushort OpCode, byte[] Body) Decode(byte[] frameBytesWithoutLength)
        {
            ushort opCode = BitConverter.ToUInt16(frameBytesWithoutLength, 0);
            var body = new byte[frameBytesWithoutLength.Length - OpCodeFieldSize];
            Array.Copy(frameBytesWithoutLength, OpCodeFieldSize, body, 0, body.Length);

            return (opCode, body);
        }

        // 스트림에서 프레임 하나를 비동기로 읽는다. 스트림이 끊기면 null을 반환한다.
        // Length가 규격을 벗어나면(OpCode조차 담을 수 없거나 상한 초과) InvalidDataException을 던진다 - 손상/조작된 스트림은
        // 이후 경계를 맞출 방법이 없어 연결을 끊어야 하고, 정상 종료(null)와 구분해 원인을 로그로 남길 수 있게 하기 위함이다.
        public static async Task<(ushort OpCode, byte[] Body)?> ReadFrameAsync(Stream stream, CancellationToken ct)
        {
            var lengthBuffer = new byte[LengthFieldSize];
            if (!await ReadExactAsync(stream, lengthBuffer, ct))
                return null;

            uint payloadLength = BitConverter.ToUInt32(lengthBuffer, 0);
            if (payloadLength < OpCodeFieldSize || payloadLength > MaxPayloadSize)
                throw new InvalidDataException($"잘못된 프레임 길이: {payloadLength}");

            var payloadBuffer = new byte[payloadLength];
            if (!await ReadExactAsync(stream, payloadBuffer, ct))
                return null;

            return Decode(payloadBuffer);
        }

        // buffer를 가득 채울 때까지 읽는다. 스트림이 도중에 끊기면 false를 반환한다.
        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
                if (read == 0)
                    return false;

                offset += read;
            }

            return true;
        }
    }
}
