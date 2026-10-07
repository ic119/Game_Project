namespace Shared.Networking.Packets
{
    // 다른 플레이어가 스킬을 시전했음을 알린다(S2C). 서버가 시전을 승인한 뒤 그 플레이어를 보고 있는 사람에게만 보낸다(본인은 이미
    // 로컬에서 재생했으므로 받지 않는다). 받는 쪽은 WeaponType/Slot으로 모션과 이펙트를 고르고, 위치/방향으로 이펙트를 놓는다.
    // 피해 숫자와 피격 모션은 기존 Game_MonsterDamageBroadcast가 타격 시점에 따로 알린다.
    public class S2CSkillCastBroadcast
    {
        public long PlayerId { get; set; }
        public int Slot { get; set; }

        // WeaponKind(= 클라이언트 WeaponType) 값.
        public int WeaponType { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(Slot);
            writer.Write(WeaponType);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        });

        public static S2CSkillCastBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CSkillCastBroadcast
        {
            PlayerId = reader.ReadInt64(),
            Slot = reader.ReadInt32(),
            WeaponType = reader.ReadInt32(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle()
        });
    }
}
