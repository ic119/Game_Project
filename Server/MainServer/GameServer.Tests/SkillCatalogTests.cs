using GameServer.Items;
using GameServer.Skills;
using Shared;

namespace GameServer.Tests;

// 실제 Skills/SkillDefinitions.json(서버가 쓰는 파일 그대로)과 로드 검증 규칙을 확인한다.
public class SkillCatalogTests
{
    private static SkillDefinition Valid(string id = "s1", int slot = 1, int unlockLevel = 3, string shape = "Line") => new()
    {
        Id = id,
        Name = id,
        WeaponType = "OneHanded",
        Slot = slot,
        UnlockLevel = unlockLevel,
        ManaCost = 10,
        CooldownSeconds = 5,
        CastLockSeconds = 0.5f,
        Shape = shape,
        Length = 3,
        Width = 1,
        Radius = 2,
        Angle = 90,
        DamageMultiplier = 1.5f,
        HitDelaySeconds = 0.2f
    };

    [Fact]
    public void RealCatalog_OneHandedHasFourSkillsUnlockingAtLevels3_6_9_12()
    {
        int[] expectedLevels = { 3, 6, 9, 12 };

        for (int slot = 1; slot <= 4; slot++)
        {
            Assert.True(SkillCatalog.TryGet(WeaponKind.OneHanded, slot, out SkillDefinition skill), $"한손검 슬롯 {slot} 없음");
            Assert.Equal(expectedLevels[slot - 1], skill.UnlockLevel);
        }
    }

    [Fact]
    public void RealCatalog_ManaCostAndCooldownGrowWithSlot()
    {
        var skills = Enumerable.Range(1, 4)
            .Select(slot => { SkillCatalog.TryGet(WeaponKind.OneHanded, slot, out SkillDefinition s); return s; })
            .ToList();

        for (int i = 1; i < skills.Count; i++)
        {
            Assert.True(skills[i].ManaCost > skills[i - 1].ManaCost);
            Assert.True(skills[i].CooldownSeconds > skills[i - 1].CooldownSeconds);
        }
    }

    [Fact]
    public void Build_AcceptsValidDefinitions()
    {
        var result = SkillCatalog.Build(new[] { Valid("a", 1, 3), Valid("b", 2, 6, "Circle"), Valid("c", 3, 9, "Cone") });

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Build_RejectsWrongUnlockLevelForSlot()
    {
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid(slot: 2, unlockLevel: 3) }));
    }

    [Fact]
    public void Build_RejectsDuplicateWeaponSlotAndDuplicateId()
    {
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid("a"), Valid("b") }));
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid("a", 1, 3), Valid("a", 2, 6) }));
    }

    [Fact]
    public void Build_RejectsUnknownShapeWeaponAndNonPositiveValues()
    {
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid(shape: "Triangle") }));
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid().with0("WeaponType", "Bow") }));
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid().with0("ManaCost", 0) }));
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid().with0("CooldownSeconds", 0f) }));
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid().with0("DamageMultiplier", 0f) }));
        Assert.Throws<InvalidOperationException>(() => SkillCatalog.Build(new[] { Valid().with0("Hits", 3) })); // 연타인데 간격 없음
    }

    [Fact]
    public void ItemCatalog_GetWeaponKind_ReadsWeaponTypeFromDefinition()
    {
        Assert.Equal(WeaponKind.OneHanded, ItemCatalog.GetWeaponKind("test_weapon_ohs"));
        Assert.Equal(WeaponKind.None, ItemCatalog.GetWeaponKind("test_weapon_common")); // 종류 미지정
        Assert.Equal(WeaponKind.None, ItemCatalog.GetWeaponKind("no_such_item"));
        Assert.Equal(WeaponKind.None, ItemCatalog.GetWeaponKind(string.Empty));
    }
}

// init 전용 속성을 한 개만 바꾼 복사본을 만드는 테스트 도우미(레코드가 아니라 with 식을 쓸 수 없다).
internal static class SkillDefinitionTestExtensions
{
    public static SkillDefinition with0(this SkillDefinition source, string property, object value)
    {
        var copy = new SkillDefinition();
        foreach (var prop in typeof(SkillDefinition).GetProperties().Where(p => p.CanWrite || p.SetMethod != null))
        {
            object? v = prop.Name == property ? value : prop.GetValue(source);
            prop.SetValue(copy, v);
        }

        return copy;
    }
}
