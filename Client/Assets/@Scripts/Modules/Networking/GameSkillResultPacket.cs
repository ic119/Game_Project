namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CSkillResult.cs의 SkillCastStatus와 값이 같아야 한다.
    public enum GameSkillCastStatus : byte
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

    // 서버 Shared/Networking/Packets/S2CSkillResult.cs와 형식이 동일해야 한다.
    // 스킬 사용 요청의 결과(본인에게만). Accepted면 CooldownSeconds는 이 스킬의 전체 쿨다운, OnCooldown이면 남은 쿨다운이다.
    public class GameSkillResultPacket
    {
        public int Slot;
        public GameSkillCastStatus Status;
        public float CooldownSeconds;

        public static GameSkillResultPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameSkillResultPacket
        {
            Slot = reader.ReadInt32(),
            Status = (GameSkillCastStatus)reader.ReadByte(),
            CooldownSeconds = reader.ReadSingle()
        });
    }
}
