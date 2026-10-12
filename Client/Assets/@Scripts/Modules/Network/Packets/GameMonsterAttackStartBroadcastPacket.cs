namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterAttackStartBroadcast.cs와 형식이 동일해야 한다.
    // 몬스터가 공격을 시작했다(선딜 시작). 이 알림으로 공격 모션을 재생한다 - 피해는 선딜이 끝난 뒤의 판정 결과로
    // 따로 온다(명중: GameMonsterAttackBroadcastPacket, 회피: GameMonsterAttackDodgedBroadcastPacket).
    public class GameMonsterAttackStartBroadcastPacket
    {
        public long MonsterId;
        public long TargetPlayerId;

        public static GameMonsterAttackStartBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterAttackStartBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64()
        });
    }
}
