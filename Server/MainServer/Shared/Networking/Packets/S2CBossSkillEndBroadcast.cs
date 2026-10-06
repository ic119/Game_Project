namespace Shared.Networking.Packets
{
    // 보스 스킬의 예고가 끝났다. Executed가 true면 예고 시간이 다 돼서 스킬이 발동한 것이고(클라이언트는 위험 범위 표시를 걷고
    // 타격/돌진/소환 연출을 재생한다), false면 발동하지 못하고 취소된 것이다(보스 사망 등 - 표시만 걷는다).
    // 피해/회피 판정은 이 알림과 별개로 Game_MonsterAttackBroadcast/Game_MonsterAttackDodgedBroadcast로 온다.
    public class S2CBossSkillEndBroadcast
    {
        public long MonsterId { get; set; }
        public byte SkillType { get; set; }
        public bool Executed { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(SkillType);
            writer.Write(Executed);
        });

        public static S2CBossSkillEndBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CBossSkillEndBroadcast
        {
            MonsterId = reader.ReadInt64(),
            SkillType = reader.ReadByte(),
            Executed = reader.ReadBoolean()
        });
    }
}
