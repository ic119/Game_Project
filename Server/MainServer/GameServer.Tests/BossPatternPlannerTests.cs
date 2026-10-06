using GameServer.Monsters;

namespace GameServer.Tests;

public class BossPatternPlannerTests
{
    // 실제 BlackKnight와 같은 구조: 100% Slam / 70% 이하 Slam+Charge / 40% 이하 Slam+Charge+Summon.
    private static BossPatternDefinition Pattern() => new()
    {
        Skills = new Dictionary<string, BossSkillDefinition>
        {
            ["Slam"] = new() { Type = BossSkillType.AreaSlam, Radius = 4f, TriggerRange = 5f },
            ["Charge"] = new() { Type = BossSkillType.Charge, Width = 2f, Length = 10f, Speed = 10f, MinRange = 3f, MaxRange = 14f },
            ["Summon"] = new() { Type = BossSkillType.Summon, SummonMonsterType = "Minion", SummonCount = 2, MaxAlive = 4, SummonRadius = 3f }
        },
        Phases = new List<BossPhaseDefinition>
        {
            new() { BelowHpPercent = 100, Skills = new() { "Slam" } },
            new() { BelowHpPercent = 70, Skills = new() { "Slam", "Charge" } },
            new() { BelowHpPercent = 40, Skills = new() { "Slam", "Charge", "Summon" } }
        }
    };

    private static readonly Func<string, bool> NoCooldown = _ => false;

    [Theory]
    [InlineData(4200, 0)]   // 가득
    [InlineData(2941, 0)]   // 70.02% -> 올림 71%라 아직 첫 구간
    [InlineData(2940, 1)]   // 정확히 70% -> 70% 이하 구간
    [InlineData(1681, 1)]   // 40.02% -> 올림 41%
    [InlineData(1680, 2)]   // 정확히 40%
    [InlineData(1, 2)]      // 1HP도 마지막 구간
    [InlineData(0, 2)]
    public void PhaseIndexFor_UsesHpPercentThresholds(int currentHp, int expectedPhase)
    {
        Assert.Equal(expectedPhase, BossPatternPlanner.PhaseIndexFor(Pattern(), currentHp, 4200));
    }

    [Fact]
    public void PickSkill_OnlyReturnsSkillsOfCurrentPhase()
    {
        BossPatternDefinition pattern = Pattern();

        // 첫 구간은 Slam뿐이다 - Charge/Summon이 조건을 만족해도 나오지 않는다.
        for (int seed = 0; seed < 50; seed++)
        {
            Assert.Equal("Slam", BossPatternPlanner.PickSkill(pattern, 0, NoCooldown, 4f, 0, new Random(seed)));
        }
    }

    [Fact]
    public void PickSkill_RespectsRanges()
    {
        BossPatternDefinition pattern = Pattern();
        var picked = new HashSet<string?>();

        // 거리 10m: Slam(5m 이내)은 안 되고 Charge(3~14m)만 된다.
        for (int seed = 0; seed < 50; seed++)
        {
            picked.Add(BossPatternPlanner.PickSkill(pattern, 1, NoCooldown, 10f, 0, new Random(seed)));
        }
        Assert.Equal(new HashSet<string?> { "Charge" }, picked);

        // 거리 2m: Charge(최소 3m)는 안 되고 Slam만 된다.
        picked.Clear();
        for (int seed = 0; seed < 50; seed++)
        {
            picked.Add(BossPatternPlanner.PickSkill(pattern, 1, NoCooldown, 2f, 0, new Random(seed)));
        }
        Assert.Equal(new HashSet<string?> { "Slam" }, picked);

        // 거리 4m: 둘 다 가능 - 무작위로 둘 다 나온다.
        picked.Clear();
        for (int seed = 0; seed < 200; seed++)
        {
            picked.Add(BossPatternPlanner.PickSkill(pattern, 1, NoCooldown, 4f, 0, new Random(seed)));
        }
        Assert.Equal(new HashSet<string?> { "Slam", "Charge" }, picked);
    }

    [Fact]
    public void PickSkill_SkipsSkillsOnCooldown_AndReturnsNullWhenNothingEligible()
    {
        BossPatternDefinition pattern = Pattern();

        Assert.Equal("Charge", BossPatternPlanner.PickSkill(pattern, 1, name => name == "Slam", 4f, 0, new Random(1)));
        Assert.Null(BossPatternPlanner.PickSkill(pattern, 1, _ => true, 4f, 0, new Random(1)));
        Assert.Null(BossPatternPlanner.PickSkill(pattern, 0, NoCooldown, 20f, 0, new Random(1))); // Slam 사거리 밖
    }

    [Fact]
    public void PickSkill_SummonOnlyWhileBelowMaxAlive()
    {
        BossPatternDefinition pattern = Pattern();
        var onlySummon = new BossPatternDefinition
        {
            Skills = pattern.Skills,
            Phases = new List<BossPhaseDefinition> { new() { BelowHpPercent = 100, Skills = new() { "Summon" } } }
        };

        Assert.Equal("Summon", BossPatternPlanner.PickSkill(onlySummon, 0, NoCooldown, 10f, 3, new Random(1)));
        Assert.Null(BossPatternPlanner.PickSkill(onlySummon, 0, NoCooldown, 10f, 4, new Random(1))); // 이미 가득
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f, 10f, 0f, 0f)]      // 선분 위의 점
    [InlineData(5f, 3f, 0f, 0f, 10f, 0f, 3f)]      // 선분 옆 수직 거리
    [InlineData(-3f, 4f, 0f, 0f, 10f, 0f, 5f)]     // 시작점 바깥 -> 시작점까지
    [InlineData(13f, 4f, 0f, 0f, 10f, 0f, 5f)]     // 끝점 바깥 -> 끝점까지
    [InlineData(3f, 4f, 0f, 0f, 0f, 0f, 5f)]       // 길이 0인 선분(이번 틱에 안 움직임)
    public void DistanceToSegment_ReturnsShortestDistance(float px, float pz, float ax, float az, float bx, float bz, float expected)
    {
        Assert.Equal(expected, BossPatternPlanner.DistanceToSegment(px, pz, ax, az, bx, bz), 3);
    }
}
