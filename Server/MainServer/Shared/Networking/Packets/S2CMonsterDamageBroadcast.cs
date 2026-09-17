namespace Shared.Networking.Packets
{
    // 플레이어 간 전투(S2CDamageBroadcast)와 달리, 몬스터의 Defense는 클라이언트가 아니라 GameServer가
    // 스폰 시점부터 직접 들고 있는 값이다(몬스터는 소유 클라이언트가 없어 신뢰할 DB가 없으므로).
    // 그래서 데미지 계산(공격력-방어력) 자체를 GameServer가 수행하고, 그 결과(RemainingHp)를 그대로 실어 보낸다 -
    // 클라이언트가 델타를 누적하는 대신 RemainingHp로 상태를 덮어써서, 패킷 유실이 있어도 다음 패킷에서 자연히 복구된다.
    public class S2CMonsterDamageBroadcast
    {
        public long MonsterId { get; set; }
        public long AttackerId { get; set; }
        public int Damage { get; set; }
        public int RemainingHp { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(AttackerId);
            writer.Write(Damage);
            writer.Write(RemainingHp);
            writer.Write(Timestamp);
        });

        public static S2CMonsterDamageBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMonsterDamageBroadcast
        {
            MonsterId = reader.ReadInt64(),
            AttackerId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            RemainingHp = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
