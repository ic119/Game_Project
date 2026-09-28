using System.Collections.Generic;

namespace Shared.Networking.Packets
{
    public class S2CEnterAck
    {
        public List<PlayerInfo> ExistingPlayers { get; set; } = new();

        // 입장한 맵(GameRoom)에 현재 살아있는 몬스터 스냅샷. ExistingPlayers 뒤에 이어 쓴다 - 플레이어 목록과
        // 동일하게 count-prefixed 리스트 형식이라 파싱 순서만 맞으면 된다.
        public List<MonsterInfo> ExistingMonsters { get; set; } = new();

        // 서버가 정한 본인 상태(위치/체력 등). 입장 위치와 체력은 서버가 정하므로(맵 데이터의 RespawnPoint, 가득 찬 체력),
        // 클라이언트는 이 값으로 자기 캐릭터를 맞춘다 - 특히 재접속 시에는 끊기기 전 위치/체력과 달라진다.
        public PlayerInfo Self { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            Self.WriteTo(writer);

            writer.Write(ExistingPlayers.Count);
            foreach (var player in ExistingPlayers)
            {
                player.WriteTo(writer);
            }

            writer.Write(ExistingMonsters.Count);
            foreach (var monster in ExistingMonsters)
            {
                monster.WriteTo(writer);
            }
        });

        public static S2CEnterAck Decode(byte[] body) => BinaryPacket.Read(body, reader =>
        {
            var self = PlayerInfo.ReadFrom(reader);

            int playerCount = reader.ReadInt32();
            var players = new List<PlayerInfo>(playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                players.Add(PlayerInfo.ReadFrom(reader));
            }

            int monsterCount = reader.ReadInt32();
            var monsters = new List<MonsterInfo>(monsterCount);
            for (int i = 0; i < monsterCount; i++)
            {
                monsters.Add(MonsterInfo.ReadFrom(reader));
            }

            return new S2CEnterAck { Self = self, ExistingPlayers = players, ExistingMonsters = monsters };
        });
    }
}
