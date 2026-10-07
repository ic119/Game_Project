using GameServer.Skills;
using Incheol.Utils;
using Shared;

namespace GameServer.Tests;

// 클라이언트 SkillTable(Utils/SkillTable.cs)이 서버 Skills/SkillDefinitions.json과 같은 값을 갖는지 검사한다.
// 클라이언트에 슬롯이 있는데 서버에 없거나(서버가 거부할 요청), 값이 다르면(마나 부족 표시/모션 길이 어긋남) 여기서 실패한다.
public class SkillTableParityTests
{
    [Fact]
    public void EveryClientSkill_MatchesServerDefinition()
    {
        Assert.NotEmpty(SkillTable.All);

        foreach (SkillTable.Entry client in SkillTable.All)
        {
            var weapon = (WeaponKind)client.WeaponIndex;
            Assert.True(SkillCatalog.TryGet(weapon, client.Slot, out SkillDefinition server), $"서버에 {weapon} 슬롯 {client.Slot}이 없음");

            Assert.Equal(server.Id, client.Id);
            Assert.Equal(server.Name, client.Name);
            Assert.Equal(server.UnlockLevel, client.UnlockLevel);
            Assert.Equal(server.ManaCost, client.ManaCost);
            Assert.Equal(server.CooldownSeconds, client.CooldownSeconds, 3);
            Assert.Equal(server.CastLockSeconds, client.CastLockSeconds, 3);
        }
    }

    [Fact]
    public void EveryServerSkill_HasClientEntry()
    {
        foreach (SkillDefinition server in SkillCatalog.All())
        {
            var weapon = Enum.Parse<WeaponKind>(server.WeaponType, ignoreCase: true);
            Assert.True(SkillTable.TryGet((int)weapon, server.Slot, out _), $"클라이언트 SkillTable에 {weapon} 슬롯 {server.Slot}이 없음");
        }
    }

    [Fact]
    public void SlotUnlockLevels_MatchServerCatalog()
    {
        for (int slot = 1; slot <= SkillTable.SlotCount; slot++)
        {
            Assert.Equal(SkillCatalog.UnlockLevelBySlot[slot], SkillTable.SlotUnlockLevels[slot]);
        }
    }

    [Fact]
    public void AnimatorSkillIndices_AreUniqueWithinWeaponAndNonZero()
    {
        foreach (var group in SkillTable.All.GroupBy(e => e.WeaponIndex))
        {
            var indices = group.Select(e => e.AnimatorSkillIndex).ToList();

            Assert.DoesNotContain(0, indices);
            Assert.Equal(indices.Count, indices.Distinct().Count());
        }
    }
}
