namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SAttackAnimationRequest.cs와 형식이 동일해야 한다.
    // 대상 유무와 무관하게 콤보 타수마다(허공 스윙 포함) 보내는 공격 모션 전용 알림.
    public class GameAttackAnimationRequestPacket
    {
        public long AttackerId;
        public int ComboStage;
        public int WeaponType;

        // 원거리 무기(완드) 투사체의 목표(연출용). 0 = 없음, 1 = 몬스터, 2 = 플레이어(Incheol.Models.Define.AttackTargetKind 값).
        // 서버는 해석하지 않고 그대로 중계하며, 다른 플레이어 화면에서 투사체가 대상을 향해 날아가게 하는 데만 쓰인다.
        public byte TargetType;
        public long TargetId;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(ComboStage);
            writer.Write(WeaponType);
            writer.Write(TargetType);
            writer.Write(TargetId);
        });
    }
}
