namespace Shared.Networking.Packets
{
    // 피격이 아닌 이유로 플레이어 체력이 바뀌었을 때(레벨업으로 최대 체력 증가 + 회복 등) 방 전체에 보낸다.
    // 피격은 S2CDamageBroadcast/S2CMonsterAttackBroadcast의 RemainingHp로, 부활은 S2CPlayerRevived로 따로 온다.
    public class S2CPlayerHpBroadcast
    {
        public long PlayerId { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(CurrentHp);
            writer.Write(MaxHp);
        });

        public static S2CPlayerHpBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CPlayerHpBroadcast
        {
            PlayerId = reader.ReadInt64(),
            CurrentHp = reader.ReadInt32(),
            MaxHp = reader.ReadInt32()
        });
    }
}
