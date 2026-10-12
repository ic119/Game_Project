using System;
using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/BinaryPacket.cs와 같은 역할 - 패킷 클래스마다 반복되던
    // MemoryStream/BinaryWriter/BinaryReader 보일러플레이트를 한 곳으로 모은다.
    public static class GameBinaryPacket
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
