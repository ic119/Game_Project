using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SAttackRequest.cs와 형식이 동일해야 한다.
    public class GameAttackRequestPacket
    {
        public long AttackerId;
        public long TargetId;
        public long Timestamp;

        public byte[] Encode()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(AttackerId);
            writer.Write(TargetId);
            writer.Write(Timestamp);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
