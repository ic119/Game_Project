namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CBossSkillEndBroadcast.cs와 형식이 동일해야 한다.
    // 보스 스킬의 예고가 끝났다. Executed가 true면 스킬이 발동한 것이고(위험 범위 표시를 걷고 타격/돌진/소환 연출을 재생),
    // false면 취소된 것이다(보스 사망 등 - 표시만 걷는다).
    public class GameBossSkillEndBroadcastPacket
    {
        public long MonsterId;
        public byte SkillType;
        public bool Executed;

        public static GameBossSkillEndBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameBossSkillEndBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            SkillType = reader.ReadByte(),
            Executed = reader.ReadBoolean()
        });
    }
}
