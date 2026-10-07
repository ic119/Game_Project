namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CSkillCastBroadcast.cs와 형식이 동일해야 한다.
    // 다른 플레이어가 스킬을 시전했음을 알린다(본인의 시전은 로컬에서 즉시 재생하므로 오지 않는다).
    public class GameSkillCastBroadcastPacket
    {
        public long PlayerId;
        public int Slot;
        public int WeaponType;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        public static GameSkillCastBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameSkillCastBroadcastPacket
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
