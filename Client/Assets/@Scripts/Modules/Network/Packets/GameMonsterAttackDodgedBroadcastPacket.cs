namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterAttackDodgedBroadcast.cs와 형식이 동일해야 한다.
    // 몬스터 공격이 대쉬 회피에 막혔다(HP 변화 없음). 대상 플레이어에게 회피 이펙트를 재생하는 데만 쓴다.
    public class GameMonsterAttackDodgedBroadcastPacket
    {
        public long MonsterId;
        public long TargetPlayerId;

        public static GameMonsterAttackDodgedBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterAttackDodgedBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64()
        });
    }
}
