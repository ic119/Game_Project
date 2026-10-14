namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CAttackAnimationBroadcast.cs와 형식이 동일해야 한다.
    // WeaponType은 공격자가 보낸 값을 서버가 그대로 중계한 것이다. RemoteCharacterController.PlayAttackAnimation은 이 값으로
    // 정확한 무기별 콤보 모션/원거리 투사체를 재생한다. 원격 캐릭터의 무기/갑옷/투구 "시각"(메시)은 이 패킷이 아니라
    // Game_EquipmentChangedBroadcast로 동기화된다(RemotePlayerManager).
    public class GameAttackAnimationBroadcastPacket
    {
        public long AttackerId;
        public int ComboStage;
        public int WeaponType;

        // 원거리 무기(완드) 투사체의 목표. 0 = 없음, 1 = 몬스터, 2 = 플레이어(Incheol.Models.Define.AttackTargetKind 값).
        // RemoteCharacterController가 이 값으로 투사체가 날아갈 대상을 찾는다.
        public byte TargetType;
        public long TargetId;

        public static GameAttackAnimationBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameAttackAnimationBroadcastPacket
        {
            AttackerId = reader.ReadInt64(),
            ComboStage = reader.ReadInt32(),
            WeaponType = reader.ReadInt32(),
            TargetType = reader.ReadByte(),
            TargetId = reader.ReadInt64()
        });
    }
}
