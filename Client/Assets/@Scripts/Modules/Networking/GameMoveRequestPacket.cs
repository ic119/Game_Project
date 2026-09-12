using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SMoveRequest.cs와 형식이 동일해야 한다.
    public class GameMoveRequestPacket
    {
        public long PlayerId;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;
        public long Timestamp;

        public byte[] Encode()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(PlayerId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
            writer.Write(Timestamp);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
