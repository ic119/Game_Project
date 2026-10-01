namespace Shared.Networking.Packets
{
    // 몬스터가 공격을 시작했다(선딜 시작). 클라이언트는 이 알림으로 공격 모션을 재생하고, 피해는 선딜이 끝난 뒤의 판정 결과로
    // 따로 온다 - 명중이면 Game_MonsterAttackBroadcast, 회피면 Game_MonsterAttackDodgedBroadcast. 선딜 동안 플레이어는
    // 대쉬로 피할 수 있다.
    public class S2CMonsterAttackStartBroadcast
    {
        public long MonsterId { get; set; }
        public long TargetPlayerId { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(TargetPlayerId);
        });

        public static S2CMonsterAttackStartBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterAttackStartBroadcast
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64()
        });
    }
}
