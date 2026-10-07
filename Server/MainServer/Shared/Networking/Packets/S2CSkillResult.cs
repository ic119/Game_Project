namespace Shared.Networking.Packets
{
    // 스킬 사용 요청의 결과(S2C, 요청한 본인에게만). 클라이언트는 Accepted일 때만 시전 모션/쿨다운 표시를 시작하고,
    // 거부되면 사유(Status)에 맞는 안내(마나 부족, 쿨다운 중 등)를 보여준다. 마나 소모는 Game_PlayerMpUpdate로 따로 온다.
    public enum SkillCastStatus : byte
    {
        Accepted = 0,
        Dead = 1,
        NoSkill = 2,
        LevelTooLow = 3,
        OnCooldown = 4,
        NotEnoughMana = 5,
        Casting = 6,
        InvalidRequest = 7
    }

    public class S2CSkillResult
    {
        public int Slot { get; set; }
        public SkillCastStatus Status { get; set; }

        // Accepted면 이 스킬의 전체 쿨다운, OnCooldown이면 남은 쿨다운(초). 그 외에는 0.
        public float CooldownSeconds { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(Slot);
            writer.Write((byte)Status);
            writer.Write(CooldownSeconds);
        });

        public static S2CSkillResult Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CSkillResult
        {
            Slot = reader.ReadInt32(),
            Status = (SkillCastStatus)reader.ReadByte(),
            CooldownSeconds = reader.ReadSingle()
        });
    }
}
