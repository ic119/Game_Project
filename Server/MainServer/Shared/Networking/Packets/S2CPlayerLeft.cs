namespace Shared.Networking.Packets
{
    public class S2CPlayerLeft
    {
        public long PlayerId { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer => writer.Write(PlayerId));

        public static S2CPlayerLeft Decode(byte[] body) =>
            BinaryPacket.Read(body, reader => new S2CPlayerLeft { PlayerId = reader.ReadInt64() });
    }
}
