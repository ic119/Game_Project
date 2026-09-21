namespace Shared.Networking.Packets
{
    // GameRoom의 몬스터 AI 틱(추적/복귀 이동)에서 위치가 바뀔 때마다 방 전체에 브로드캐스트된다.
    // S2CMoveBroadcast(플레이어)와 형식이 동일하되 대상이 MonsterId라는 점만 다르다.
    public class S2CMonsterMoveBroadcast
    {
        public long MonsterId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
            writer.Write(Timestamp);
        });

        public static S2CMonsterMoveBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterMoveBroadcast
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
