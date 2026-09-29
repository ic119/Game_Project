namespace Shared.Networking.Packets
{
    // 공격 모션 전용 알림(S2C). attackerId를 보고 있는 사람에게만 중계된다(본인은 이미 로컬에서 즉시 재생했으므로
    // 받지 않는다). 공격자가 보낸 WeaponType을 그대로 중계한다 - 원격 플레이어의 장착 무기 "시각"(메시) 자체는
    // 아직 동기화되지 않지만(RemotePlayerManager가 원격 캐릭터에 EquipItem을 호출하지 않음), 애니메이션만큼은
    // 실제 무기 타입에 맞게 재생한다(콤보 타이밍/모션 종류는 정확해진다).
    public class S2CAttackAnimationBroadcast
    {
        public long AttackerId { get; set; }
        public int ComboStage { get; set; }
        public int WeaponType { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(ComboStage);
            writer.Write(WeaponType);
        });

        public static S2CAttackAnimationBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CAttackAnimationBroadcast
        {
            AttackerId = reader.ReadInt64(),
            ComboStage = reader.ReadInt32(),
            WeaponType = reader.ReadInt32()
        });
    }
}
