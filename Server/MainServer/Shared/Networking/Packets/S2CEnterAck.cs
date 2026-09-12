using System.Collections.Generic;

namespace Shared.Networking.Packets
{
    public class S2CEnterAck
    {
        public List<PlayerInfo> ExistingPlayers { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ExistingPlayers.Count);
            foreach (var player in ExistingPlayers)
            {
                player.WriteTo(writer);
            }
        });

        public static S2CEnterAck Decode(byte[] body) => BinaryPacket.Read(body, reader =>
        {
            int count = reader.ReadInt32();
            var players = new List<PlayerInfo>(count);
            for (int i = 0; i < count; i++)
            {
                players.Add(PlayerInfo.ReadFrom(reader));
            }
            return new S2CEnterAck { ExistingPlayers = players };
        });
    }
}
