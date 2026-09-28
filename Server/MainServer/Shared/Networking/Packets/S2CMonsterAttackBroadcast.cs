namespace Shared.Networking.Packets
{
    // 몬스터가 근접 사거리 안의 플레이어를 공격할 때(GameRoom.TickChasing/AttackPlayerAsync)마다 온다.
    // S2CDamageBroadcast(PvP)와 동일하게 Damage는 서버가 방어력까지 적용한 최종 피해량이고, RemainingHp는
    // 서버가 들고 있는 대상의 남은 체력이다 - 클라이언트는 계산하지 않고 이 값을 그대로 표시한다.
    // RemainingHp가 0이면 이 공격으로 대상이 사망했다는 뜻이다(부활은 S2CPlayerRevived로 온다).
    public class S2CMonsterAttackBroadcast
    {
        public long MonsterId { get; set; }
        public long TargetPlayerId { get; set; }
        public int Damage { get; set; }
        public int RemainingHp { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(TargetPlayerId);
            writer.Write(Damage);
            writer.Write(RemainingHp);
            writer.Write(Timestamp);
        });

        public static S2CMonsterAttackBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterAttackBroadcast
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            RemainingHp = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
