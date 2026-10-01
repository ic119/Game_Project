namespace Shared.Networking.Packets
{
    // 몬스터 공격이 대쉬 회피에 막혔다. 피해는 없다(HP 변화 없음) - 클라이언트가 대상 플레이어에게 회피 이펙트만 재생한다.
    // 대쉬와 상관없이 그냥 사거리를 벗어나 빗나간 경우에는 보내지 않는다(GameRoom.ResolveMonsterAttack 참고).
    public class S2CMonsterAttackDodgedBroadcast
    {
        public long MonsterId { get; set; }
        public long TargetPlayerId { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(TargetPlayerId);
        });

        public static S2CMonsterAttackDodgedBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterAttackDodgedBroadcast
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64()
        });
    }
}
