namespace Shared.Networking.Packets
{
    // 공격 모션 전용 알림(S2C). attackerId를 보고 있는 사람에게만 중계된다(본인은 이미 로컬에서 즉시 재생했으므로
    // 받지 않는다). 공격자가 보낸 WeaponType을 그대로 중계해 콤보 타이밍/모션 종류/원거리 투사체를 실제 무기에 맞게 재생한다.
    // 원격 캐릭터의 무기/갑옷/투구 "시각"(메시)은 이 패킷이 아니라 Game_EquipmentChangedBroadcast로 동기화된다.
    public class S2CAttackAnimationBroadcast
    {
        public long AttackerId { get; set; }
        public int ComboStage { get; set; }
        public int WeaponType { get; set; }

        // 원거리 무기(완드)의 투사체 목표(C2SAttackAnimationRequest가 보낸 값 그대로). 0 = 없음, 1 = 몬스터, 2 = 플레이어.
        public byte TargetType { get; set; }
        public long TargetId { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(ComboStage);
            writer.Write(WeaponType);
            writer.Write(TargetType);
            writer.Write(TargetId);
        });

        public static S2CAttackAnimationBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CAttackAnimationBroadcast
        {
            AttackerId = reader.ReadInt64(),
            ComboStage = reader.ReadInt32(),
            WeaponType = reader.ReadInt32(),
            TargetType = reader.ReadByte(),
            TargetId = reader.ReadInt64()
        });
    }
}
