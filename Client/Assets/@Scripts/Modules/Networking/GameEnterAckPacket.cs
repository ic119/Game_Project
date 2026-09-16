using System.Collections.Generic;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CEnterAck.cs와 형식이 동일해야 한다.
    public class GameEnterAckPacket
    {
        public List<GamePlayerInfo> ExistingPlayers = new();

        public static GameEnterAckPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
        {
            int count = reader.ReadInt32();
            var players = new List<GamePlayerInfo>(count);
            for (int i = 0; i < count; i++)
            {
                players.Add(GamePlayerInfo.ReadFrom(reader));
            }

            return new GameEnterAckPacket { ExistingPlayers = players };
        });
    }
}
