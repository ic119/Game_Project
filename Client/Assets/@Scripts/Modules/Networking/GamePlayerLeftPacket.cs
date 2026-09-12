using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerLeft.cs와 형식이 동일해야 한다.
    public class GamePlayerLeftPacket
    {
        public long PlayerId;

        public static GamePlayerLeftPacket Decode(byte[] body)
        {
            using var stream = new MemoryStream(body);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            return new GamePlayerLeftPacket { PlayerId = reader.ReadInt64() };
        }
    }
}
