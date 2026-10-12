using System.Collections.Generic;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CEnterAck.cs와 형식이 동일해야 한다.
    public class GameEnterAckPacket
    {
        // 서버가 정한 본인 상태(위치/체력 등). 입장(특히 재접속) 시 자기 캐릭터를 이 값으로 맞춘다.
        public GamePlayerInfo Self = new();
        public List<GamePlayerInfo> ExistingPlayers = new();
        public List<GameMonsterInfo> ExistingMonsters = new();

        public static GameEnterAckPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
        {
            var self = GamePlayerInfo.ReadFrom(reader);

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

            return new GameEnterAckPacket { Self = self, ExistingPlayers = players, ExistingMonsters = monsters };
        });
    }
}
