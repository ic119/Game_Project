namespace Shared.Networking.Packets
{
    public class C2SAttackRequest
    {
        public long AttackerId { get; set; }
        public long TargetId { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(TargetId);
            writer.Write(Timestamp);
        });

        public static C2SAttackRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SAttackRequest
        {
            AttackerId = reader.ReadInt64(),
            TargetId = reader.ReadInt64(),
            Timestamp = reader.ReadInt64()
        });
    }
}
