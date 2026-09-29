namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SAttackAnimationRequest.cs와 형식이 동일해야 한다.
    // 대상 유무와 무관하게 콤보 타수마다(허공 스윙 포함) 보내는 공격 모션 전용 알림.
    public class GameAttackAnimationRequestPacket
    {
        public long AttackerId;
        public int ComboStage;
        public int WeaponType;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(ComboStage);
            writer.Write(WeaponType);
        });
    }
}
