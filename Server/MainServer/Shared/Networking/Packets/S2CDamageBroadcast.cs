namespace Shared.Networking.Packets
{
    // Damage는 방어력 적용 전 원본 공격력(attacker의 PlayerInfo.AttackPower)이다.
    // 방어력 차감은 각 클라이언트가 로컬로 들고 있는 target의 실제 Defense로 직접 계산한다
    // (Move/Chat과 동일하게 GameServer는 스탯 연산을 하지 않고 검증 후 그대로 중계만 한다).
    public class S2CDamageBroadcast
    {
        public long AttackerId { get; set; }
        public long TargetId { get; set; }
        public int Damage { get; set; }
        public long Timestamp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(TargetId);
            writer.Write(Damage);
            writer.Write(Timestamp);
        });

        public static S2CDamageBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CDamageBroadcast
        {
            AttackerId = reader.ReadInt64(),
            TargetId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
