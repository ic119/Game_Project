using GameServer.Monsters;

namespace GameServer.Tests;

// MonsterDefinitionCatalog가 보스 패턴을 부팅 시점에 검증하는 규칙. 데이터 오타(없는 스킬 참조, 너무 짧은 예고 등)가
// 보스 전투 도중이 아니라 서버 시작 때 바로 드러나게 한다.
public class BossPatternCatalogTests
{
    private const string Minion = """ "Minion": { "maxHp": 10 } """;

    // 보스 하나(+ 하수인 정의)를 담은 JSON. skills/phases 본문만 바꿔 끼운다.
    private static string BossJson(string skills, string phases, string extraDefinitions = Minion) =>
        "{ " + extraDefinitions + ", \"Boss\": { \"maxHp\": 1000, \"bossPattern\": { \"skills\": { " + skills + " }, \"phases\": [ " + phases + " ] } } }";

    private const string ValidSlam = """ "Slam": { "type": "AreaSlam", "telegraphSeconds": 1.0, "radius": 4, "triggerRange": 5 } """;
    private const string ValidCharge = """ "Charge": { "type": "Charge", "telegraphSeconds": 1.0, "width": 2, "length": 10, "speed": 10, "minRange": 2, "maxRange": 12 } """;
    private const string ValidSummon = """ "Summon": { "type": "Summon", "telegraphSeconds": 1.0, "summonMonsterType": "Minion", "summonCount": 2, "maxAlive": 4, "summonRadius": 3 } """;
    private const string Phase100Slam = """ { "belowHpPercent": 100, "skills": [ "Slam" ] } """;

    private static void AssertInvalid(string json) =>
        Assert.Throws<InvalidOperationException>(() => MonsterDefinitionCatalog.ParseAndValidate(json, "test"));

    [Fact]
    public void ValidBossPattern_ParsesAndSortsPhasesDescending()
    {
        // 구간을 일부러 뒤섞어 적어도 내림차순(100이 먼저)으로 정렬된다.
        string phases = """ { "belowHpPercent": 40, "skills": [ "Slam", "Charge", "Summon" ] }, { "belowHpPercent": 100, "skills": [ "Slam" ] }, { "belowHpPercent": 70, "skills": [ "Slam", "Charge" ] } """;
        var result = MonsterDefinitionCatalog.ParseAndValidate(BossJson($"{ValidSlam}, {ValidCharge}, {ValidSummon}", phases), "test");

        BossPatternDefinition pattern = result["Boss"].BossPattern!;
        Assert.Equal(new[] { 100, 70, 40 }, pattern.Phases.Select(p => p.BelowHpPercent));
        Assert.Equal(BossSkillType.Summon, pattern.Skills["Summon"].Type);
        Assert.Equal(4f, pattern.Skills["Slam"].Radius);
        Assert.Null(result["Minion"].BossPattern);
    }

    [Fact]
    public void FirstPhaseNotAt100_Throws()
    {
        AssertInvalid(BossJson(ValidSlam, """ { "belowHpPercent": 70, "skills": [ "Slam" ] } """));
    }

    [Fact]
    public void DuplicatePhaseThreshold_Throws()
    {
        AssertInvalid(BossJson(ValidSlam, $"{Phase100Slam}, {{ \"belowHpPercent\": 100, \"skills\": [ \"Slam\" ] }}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PhaseThresholdOutOfRange_Throws(int percent)
    {
        AssertInvalid(BossJson(ValidSlam, $"{Phase100Slam}, {{ \"belowHpPercent\": {percent}, \"skills\": [ \"Slam\" ] }}"));
    }

    [Fact]
    public void PhaseReferencesUnknownSkill_Throws()
    {
        AssertInvalid(BossJson(ValidSlam, """ { "belowHpPercent": 100, "skills": [ "Slam", "Typo" ] } """));
    }

    [Fact]
    public void PhaseWithNoSkills_Throws()
    {
        AssertInvalid(BossJson(ValidSlam, """ { "belowHpPercent": 100, "skills": [ ] } """));
    }

    [Fact]
    public void NoSkillsOrNoPhases_Throws()
    {
        AssertInvalid("""{ "Boss": { "maxHp": 10, "bossPattern": { "skills": { }, "phases": [ ] } } }""");
    }

    [Fact]
    public void TelegraphShorterThanMinimum_Throws()
    {
        // 0.3초 미만 예고는 플레이어가 반응할 수 없다.
        string tooShort = """ "Slam": { "type": "AreaSlam", "telegraphSeconds": 0.1, "radius": 4, "triggerRange": 5 } """;
        AssertInvalid(BossJson(tooShort, Phase100Slam));
    }

    [Theory]
    [InlineData("""{ "type": "AreaSlam", "telegraphSeconds": 1, "radius": 0, "triggerRange": 5 }""")]
    [InlineData("""{ "type": "AreaSlam", "telegraphSeconds": 1, "radius": 4, "triggerRange": 0 }""")]
    [InlineData("""{ "type": "AreaSlam", "telegraphSeconds": 1, "radius": 4, "triggerRange": 5, "damageMultiplier": 0 }""")]
    [InlineData("""{ "type": "AreaSlam", "telegraphSeconds": 1, "radius": 4, "triggerRange": 5, "cooldownSeconds": -1 }""")]
    public void InvalidAreaSlam_Throws(string skillJson)
    {
        AssertInvalid(BossJson($"\"Slam\": {skillJson}", Phase100Slam));
    }

    [Theory]
    [InlineData("""{ "type": "Charge", "telegraphSeconds": 1, "width": 0, "length": 10, "speed": 10, "minRange": 2, "maxRange": 12 }""")]
    [InlineData("""{ "type": "Charge", "telegraphSeconds": 1, "width": 2, "length": 0, "speed": 10, "minRange": 2, "maxRange": 12 }""")]
    [InlineData("""{ "type": "Charge", "telegraphSeconds": 1, "width": 2, "length": 10, "speed": 0, "minRange": 2, "maxRange": 12 }""")]
    [InlineData("""{ "type": "Charge", "telegraphSeconds": 1, "width": 2, "length": 10, "speed": 10, "minRange": 12, "maxRange": 12 }""")]
    [InlineData("""{ "type": "Charge", "telegraphSeconds": 1, "width": 2, "length": 10, "speed": 10, "minRange": -1, "maxRange": 12 }""")]
    public void InvalidCharge_Throws(string skillJson)
    {
        AssertInvalid(BossJson($"\"Slam\": {skillJson}", Phase100Slam));
    }

    [Theory]
    [InlineData("""{ "type": "Summon", "telegraphSeconds": 1, "summonMonsterType": "Minion", "summonCount": 0, "maxAlive": 4, "summonRadius": 3 }""")]
    [InlineData("""{ "type": "Summon", "telegraphSeconds": 1, "summonMonsterType": "Minion", "summonCount": 5, "maxAlive": 4, "summonRadius": 3 }""")]
    [InlineData("""{ "type": "Summon", "telegraphSeconds": 1, "summonMonsterType": "Minion", "summonCount": 2, "maxAlive": 4, "summonRadius": 0 }""")]
    [InlineData("""{ "type": "Summon", "telegraphSeconds": 1, "summonMonsterType": "NoSuchMonster", "summonCount": 2, "maxAlive": 4, "summonRadius": 3 }""")]
    public void InvalidSummon_Throws(string skillJson)
    {
        AssertInvalid(BossJson($"\"Slam\": {skillJson}", Phase100Slam));
    }

    [Fact]
    public void Summon_CannotSummonABoss()
    {
        // 다른 보스를 소환하거나 자기 자신을 소환하는 것(재귀 소환)은 막는다.
        const string otherBoss = """ "OtherBoss": { "maxHp": 10, "bossPattern": { "skills": { "Slam": { "type": "AreaSlam", "telegraphSeconds": 1, "radius": 4, "triggerRange": 5 } }, "phases": [ { "belowHpPercent": 100, "skills": [ "Slam" ] } ] } } """;
        string summonsOtherBoss = """ "S": { "type": "Summon", "telegraphSeconds": 1, "summonMonsterType": "OtherBoss", "summonCount": 1, "maxAlive": 1, "summonRadius": 3 } """;
        AssertInvalid(BossJson(summonsOtherBoss, """ { "belowHpPercent": 100, "skills": [ "S" ] } """, otherBoss));

        string summonsSelf = """ "S": { "type": "Summon", "telegraphSeconds": 1, "summonMonsterType": "Boss", "summonCount": 1, "maxAlive": 1, "summonRadius": 3 } """;
        AssertInvalid(BossJson(summonsSelf, """ { "belowHpPercent": 100, "skills": [ "S" ] } """));
    }

    [Fact]
    public void UnknownSkillTypeName_Throws()
    {
        AssertInvalid(BossJson(""" "Slam": { "type": "Teleport", "telegraphSeconds": 1 } """, Phase100Slam));
    }
}
