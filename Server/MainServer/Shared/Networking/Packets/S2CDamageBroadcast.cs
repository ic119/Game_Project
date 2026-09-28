namespace Shared.Networking.Packets
{
    // PvP 공격 결과. Damage는 GameServer가 공격자 AttackPower와 대상 Defense로 계산한 최종 피해량이고,
    // RemainingHp는 서버가 들고 있는 대상의 남은 체력이다 - 플레이어 HP는 서버가 유일한 권위이므로
    // 클라이언트는 계산하지 않고 이 값을 그대로 표시한다. RemainingHp가 0이면 이 공격으로 대상이 사망했다는 뜻이다.
    public class S2CDamageBroadcast
    {
        public long AttackerId { get; set; }
        public long TargetId { get; set; }
        public int Damage { get; set; }
        public int RemainingHp { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(TargetId);
            writer.Write(Damage);
            writer.Write(RemainingHp);
            writer.Write(Timestamp);
        });

        public static S2CDamageBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CDamageBroadcast
        {
            AttackerId = reader.ReadInt64(),
            TargetId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            RemainingHp = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
