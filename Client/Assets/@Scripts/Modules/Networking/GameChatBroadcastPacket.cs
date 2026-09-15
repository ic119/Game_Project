using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CChatBroadcast.cs와 형식이 동일해야 한다.
    public class GameChatBroadcastPacket
    {
        public long PlayerId;
        public string Nickname = string.Empty;
        public string Message = string.Empty;
        public long Timestamp;

        public static GameChatBroadcastPacket Decode(byte[] body)
        {
            using var stream = new MemoryStream(body);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            return new GameChatBroadcastPacket
            {
                PlayerId = reader.ReadInt64(),
                Nickname = reader.ReadString(),
                Message = reader.ReadString(),
                Timestamp = reader.ReadInt64()
            };
        }
    }
}
