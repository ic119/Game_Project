namespace Shared.Networking.Packets
{
    public class S2CChatBroadcast
    {
        public long PlayerId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(Nickname);
            writer.Write(Message);
            writer.Write(Timestamp);
        });

        public static S2CChatBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CChatBroadcast
        {
            PlayerId = reader.ReadInt64(),
            Nickname = reader.ReadString(),
            Message = reader.ReadString(),
            Timestamp = reader.ReadInt64()
        });
    }
}
