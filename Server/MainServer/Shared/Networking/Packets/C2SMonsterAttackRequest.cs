namespace Shared.Networking.Packets
{
    public class C2SMonsterAttackRequest
    {
        public long AttackerId { get; set; }
        public long MonsterId { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(MonsterId);
            writer.Write(Timestamp);
        });

        public static C2SMonsterAttackRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SMonsterAttackRequest
        {
            AttackerId = reader.ReadInt64(),
            MonsterId = reader.ReadInt64(),
            Timestamp = reader.ReadInt64()
        });
    }
}
