using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerJoined.cs와 형식이 동일해야 한다.
    public class GamePlayerJoinedPacket
    {
        public GamePlayerInfo Player = new();

        public static GamePlayerJoinedPacket Decode(byte[] body)
        {
            using var stream = new MemoryStream(body);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            return new GamePlayerJoinedPacket { Player = GamePlayerInfo.ReadFrom(reader) };
        }
    }
}
