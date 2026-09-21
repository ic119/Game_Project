namespace Shared.Networking.Packets
{
    // 몬스터가 근접 사거리 안의 플레이어를 공격할 때(GameRoom.TickChasing/AttackPlayerAsync)마다 온다.
    // Damage는 S2CDamageBroadcast(PvP)와 동일하게 방어력 적용 전 원본 공격력이다 - 대상 클라이언트가
    // 로컬 Defense로 직접 계산해 HealthComponent.TakeDamage에 그대로 흘려보낼 수 있게 하기 위함이다.
    // (서버 자신의 PlayerInfo.CurrentHp는 AI의 사망 판정을 위해 별도로 방어력을 적용해 갱신해둔다.)
    public class S2CMonsterAttackBroadcast
    {
        public long MonsterId { get; set; }
        public long TargetPlayerId { get; set; }
        public int Damage { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(TargetPlayerId);
            writer.Write(Damage);
            writer.Write(Timestamp);
        });

        public static S2CMonsterAttackBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterAttackBroadcast
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
