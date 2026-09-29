namespace Shared.Networking.Packets
{
    // 공격 모션 전용 알림(C2S). Game_AttackRequest/Game_MonsterAttackRequest는 판정 반경 안에 대상이 있어야만
    // 보내지만, 이건 콤보 타수마다(허공 스윙 포함) 무조건 보낸다 - 근처 다른 플레이어가 내 스윙 모션 자체를
    // 볼 수 있어야 하기 때문이다. 서버는 데미지/쿨다운을 판정하지 않고 그대로 중계만 한다.
    public class C2SAttackAnimationRequest
    {
        public long AttackerId { get; set; }
        public int ComboStage { get; set; }

        // 클라이언트 WeaponType enum(Client Define.cs) 값 그대로. 서버는 의미를 해석하지 않고 그대로 중계만
        // 한다(ComboStage와 같은 성격) - 연출용이라 위조돼도 다른 플레이어 화면에 잘못된 무기 모션이 보이는
        // 것 이상의 피해가 없다.
        public int WeaponType { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(ComboStage);
            writer.Write(WeaponType);
        });

        public static C2SAttackAnimationRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SAttackAnimationRequest
        {
            AttackerId = reader.ReadInt64(),
            ComboStage = reader.ReadInt32(),
            WeaponType = reader.ReadInt32()
        });
    }
}
