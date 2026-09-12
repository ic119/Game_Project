namespace Shared.Networking.Packets
{
    public class S2CMoveBroadcast
    {
        public long PlayerId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
            writer.Write(Timestamp);
        });

        public static S2CMoveBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMoveBroadcast
        {
            PlayerId = reader.ReadInt64(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle(),
            Timestamp = reader.ReadInt64()
        });
    }
}
