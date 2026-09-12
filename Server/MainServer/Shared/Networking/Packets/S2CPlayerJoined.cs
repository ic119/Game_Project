namespace Shared.Networking.Packets
{
    public class S2CPlayerJoined
    {
        public PlayerInfo Player { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer => Player.WriteTo(writer));

        public static S2CPlayerJoined Decode(byte[] body) =>
            BinaryPacket.Read(body, reader => new S2CPlayerJoined { Player = PlayerInfo.ReadFrom(reader) });
    }
}
