namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterSpawnBroadcast.cs와 형식이 동일해야 한다.
    // 리스폰으로 새 몬스터 개체가 태어났을 때만 온다 - 입장 시점의 기존 몬스터 스냅샷은
    // GameEnterAckPacket.ExistingMonsters로 별도 전달된다.
    public class GameMonsterSpawnBroadcastPacket
    {
        public GameMonsterInfo Monster = new();

        public static GameMonsterSpawnBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
            new GameMonsterSpawnBroadcastPacket { Monster = GameMonsterInfo.ReadFrom(reader) });
    }
}
