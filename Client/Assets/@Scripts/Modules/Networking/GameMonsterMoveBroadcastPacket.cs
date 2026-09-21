namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterMoveBroadcast.cs와 형식이 동일해야 한다.
    public class GameMonsterMoveBroadcastPacket
    {
        public long MonsterId;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;
        public long Timestamp;

        public static GameMonsterMoveBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterMoveBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle(),
            Timestamp = reader.ReadInt64()
        });
    }
}
