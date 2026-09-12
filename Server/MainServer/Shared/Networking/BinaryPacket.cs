using System;
using System.IO;
using System.Text;

namespace Shared.Networking
{
    // MessagePack 대신 BinaryWriter/BinaryReader로 패킷 바디를 직접 인코딩한다.
    // 두 타입 모두 BCL 표준 포맷(문자열은 7비트 인코딩 길이 접두 + UTF8)이라 .NET(서버)과
    // Unity Mono(클라이언트) 사이에 별도 패키지 없이도 바이트 단위로 호환된다.
    public static class BinaryPacket
    {
        public static byte[] Write(Action<BinaryWriter> writeAction)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writeAction(writer);
            writer.Flush();
            return stream.ToArray();
        }

        public static T Read<T>(byte[] body, Func<BinaryReader, T> readAction)
        {
            using var stream = new MemoryStream(body);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            return readAction(reader);
        }
    }
}
