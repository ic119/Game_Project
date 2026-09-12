using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMoveBroadcast.cs와 형식이 동일해야 한다.
    public class GameMoveBroadcastPacket
    {
        public long PlayerId;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;
        public long Timestamp;

        public static GameMoveBroadcastPacket Decode(byte[] body)
        {
            using var stream = new MemoryStream(body);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            return new GameMoveBroadcastPacket
            {
                PlayerId = reader.ReadInt64(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                RotationY = reader.ReadSingle(),
                Timestamp = reader.ReadInt64()
            };
        }
    }
}
