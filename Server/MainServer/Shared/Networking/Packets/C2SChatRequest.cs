namespace Shared.Networking.Packets
{
    public class C2SChatRequest
    {
        public long PlayerId { get; set; }
        public string Message { get; set; } = string.Empty;
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(Message);
            writer.Write(Timestamp);
        });

        public static C2SChatRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SChatRequest
        {
            PlayerId = reader.ReadInt64(),
            Message = reader.ReadString(),
            Timestamp = reader.ReadInt64()
        });
    }
}
