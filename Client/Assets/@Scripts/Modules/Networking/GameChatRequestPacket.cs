using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SChatRequest.cs와 형식이 동일해야 한다.
    public class GameChatRequestPacket
    {
        public long PlayerId;
        public string Message = string.Empty;
        public long Timestamp;

        public byte[] Encode()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(PlayerId);
            writer.Write(Message);
            writer.Write(Timestamp);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
