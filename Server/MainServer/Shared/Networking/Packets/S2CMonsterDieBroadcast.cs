namespace Shared.Networking.Packets
{
    public class S2CMonsterDieBroadcast
    {
        public long MonsterId { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(Timestamp);
        });

        public static S2CMonsterDieBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterDieBroadcast
        {
            MonsterId = reader.ReadInt64(),
            Timestamp = reader.ReadInt64()
        });
    }
}
