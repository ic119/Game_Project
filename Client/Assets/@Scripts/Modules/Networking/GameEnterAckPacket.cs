using System.Collections.Generic;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CEnterAck.cs와 형식이 동일해야 한다.
    public class GameEnterAckPacket
    {
        public List<GamePlayerInfo> ExistingPlayers = new();
        public List<GameMonsterInfo> ExistingMonsters = new();

        public static GameEnterAckPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
        {
            int playerCount = reader.ReadInt32();
            var players = new List<GamePlayerInfo>(playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                players.Add(GamePlayerInfo.ReadFrom(reader));
            }

            int monsterCount = reader.ReadInt32();
            var monsters = new List<GameMonsterInfo>(monsterCount);
            for (int i = 0; i < monsterCount; i++)
            {
                monsters.Add(GameMonsterInfo.ReadFrom(reader));
            }

            return new GameEnterAckPacket { ExistingPlayers = players, ExistingMonsters = monsters };
        });
    }
}
