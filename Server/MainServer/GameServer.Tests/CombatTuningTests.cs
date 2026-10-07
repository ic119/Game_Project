using GameServer.Combat;
using Incheol.Utils;
using Microsoft.Extensions.Configuration;

namespace GameServer.Tests;

public class CombatTuningTests
{
    private static CombatTuning LoadFrom(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return CombatTuning.Load(configuration.GetSection("Combat"));
    }

    [Fact]
    public void Defaults_AreValid()
    {
        new CombatTuning().Validate();
        LoadFrom(new Dictionary<string, string?>()); // 설정이 없어도 기본값으로 로드된다.
    }

    [Fact]
    public void ShippedAppSettings_MatchDefaults()
    {
        // appsettings.json의 Combat 섹션은 기본값을 그대로 적어 둔 것이다 - 한쪽만 바뀌면 서버 동작이 문서와 달라진다.
        IConfiguration configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false).Build();
        CombatTuning loaded = CombatTuning.Load(configuration.GetSection("Combat"));
        var defaults = new CombatTuning();

        Assert.Equal(defaults.DashInvulnerableSeconds, loaded.DashInvulnerableSeconds);
        Assert.Equal(defaults.MinDashIntervalSeconds, loaded.MinDashIntervalSeconds);
        Assert.Equal(defaults.MonsterMeleeRange, loaded.MonsterMeleeRange);
        Assert.Equal(defaults.MonsterAttackIntervalSeconds, loaded.MonsterAttackIntervalSeconds);
        Assert.Equal(defaults.MonsterAttackWindupSeconds, loaded.MonsterAttackWindupSeconds);
        Assert.Equal(defaults.ReviveDelaySeconds, loaded.ReviveDelaySeconds);
        Assert.Equal(defaults.MinAttackIntervalMs, loaded.MinAttackIntervalMs);
        Assert.Equal(defaults.MaxAttackRange, loaded.MaxAttackRange);
        Assert.Equal(defaults.MinUseItemIntervalMs, loaded.MinUseItemIntervalMs);
        Assert.Equal(defaults.ManaRegenPercentPerSecond, loaded.ManaRegenPercentPerSecond);
    }

    [Fact]
    public void ManaRegen_ZeroDisablesRegen_AndOutOfRangeIsRejected()
    {
        Assert.Equal(0, LoadFrom(new() { ["Combat:ManaRegenPercentPerSecond"] = "0" }).ManaRegenPercentPerSecond);
        Assert.Throws<InvalidOperationException>(() => LoadFrom(new() { ["Combat:ManaRegenPercentPerSecond"] = "-1" }));
        Assert.Throws<InvalidOperationException>(() => LoadFrom(new() { ["Combat:ManaRegenPercentPerSecond"] = "51" }));
    }

    [Fact]
    public void Load_AppliesConfiguredValues()
    {
        CombatTuning tuning = LoadFrom(new()
        {
            ["Combat:MonsterAttackWindupSeconds"] = "0.6",
            ["Combat:ReviveDelaySeconds"] = "8",
        });

        Assert.Equal(0.6f, tuning.MonsterAttackWindupSeconds);
        Assert.Equal(8, tuning.ReviveDelaySeconds);
        Assert.Equal(new CombatTuning().DashInvulnerableSeconds, tuning.DashInvulnerableSeconds); // 나머지는 기본값
    }

    [Fact]
    public void Load_RejectsUnknownKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LoadFrom(new() { ["Combat:MonsterWindupSeconds"] = "0.5" }));
        Assert.Contains("MonsterWindupSeconds", ex.Message);
    }

    [Theory]
    [InlineData("MonsterAttackWindupSeconds", "0")]
    [InlineData("MonsterAttackWindupSeconds", "-1")]
    [InlineData("ReviveDelaySeconds", "999")]
    [InlineData("MaxAttackRange", "0.1")]
    [InlineData("DashInvulnerableSeconds", "abc")]
    public void Load_RejectsOutOfRangeOrNonNumeric(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => LoadFrom(new() { [$"Combat:{key}"] = value }));
    }

    [Fact]
    public void Load_RejectsDashInvulnerableLongerThanMinInterval()
    {
        // 무적이 최소 대쉬 간격보다 길면 쿨다운 없이 계속 무적이 된다.
        Assert.Throws<InvalidOperationException>(() => LoadFrom(new()
        {
            ["Combat:DashInvulnerableSeconds"] = "1.5",
            ["Combat:MinDashIntervalSeconds"] = "1.0",
        }));
    }

    [Fact]
    public void Load_RejectsWindupNotShorterThanAttackInterval()
    {
        Assert.Throws<InvalidOperationException>(() => LoadFrom(new()
        {
            ["Combat:MonsterAttackWindupSeconds"] = "2.0",
            ["Combat:MonsterAttackIntervalSeconds"] = "1.5",
        }));
    }

    // ----- 클라이언트(CombatTimings)와 맞물리는 관계. 어느 한쪽 값을 바꾸면 여기서 걸린다. -----

    [Fact]
    public void Defaults_DashInvulnerabilityCoversWholeClientDash()
    {
        Assert.True(new CombatTuning().DashInvulnerableSeconds >= CombatTimings.DashDurationSeconds,
            "서버 대쉬 무적 시간이 클라이언트 대쉬 지속시간보다 짧으면 대쉬 끝부분에서 맞는다.");
    }

    [Fact]
    public void Defaults_MinDashIntervalDoesNotRejectNormalConsecutiveDashes()
    {
        // 정상 연속 대쉬의 시작 간격은 지속시간 + 쿨다운이다. 서버 최소 간격이 이보다 길면 정상 대쉬가 거부된다.
        Assert.True(new CombatTuning().MinDashIntervalSeconds <= CombatTimings.DashDurationSeconds + CombatTimings.DashCooldownSeconds);
    }

    [Fact]
    public void Defaults_ReviveDelayMatchesClientCountdown()
    {
        Assert.Equal(CombatTimings.ReviveDelaySeconds, (float)new CombatTuning().ReviveDelaySeconds);
    }

    [Fact]
    public void Defaults_MinAttackIntervalIsShorterThanClientComboGuard()
    {
        Assert.True(new CombatTuning().MinAttackIntervalMs < CombatTimings.ComboInputGuardSeconds * 1000f,
            "서버 최소 공격 간격이 클라이언트 콤보 입력 가드보다 길면 정상 콤보 2타가 서버에서 드롭된다.");
    }

    // ----- 무기 공격 길이 표 -----

    [Theory]
    [InlineData(1, 1, 16f / 30f)]  // OneHanded 1타
    [InlineData(2, 2, 18f / 30f)]  // TwoHanded 2타
    [InlineData(5, 1, 16f / 30f)]  // Spear 1타
    [InlineData(5, 2, 20f / 30f)]  // Spear 2타(1타와 길이가 다르다)
    public void AttackTable_KnownWeapons(int weaponIndex, int stage, float expected)
    {
        Assert.True(CombatTimings.TryGetAttackDuration(weaponIndex, stage, maxComboStage: 2, out float seconds));
        Assert.Equal(expected, seconds, 5);
    }

    [Fact]
    public void AttackTable_UnknownWeapon_FallsBackToDefault()
    {
        Assert.False(CombatTimings.TryGetAttackDuration(3, 1, maxComboStage: 2, out float seconds)); // 3은 제거된 Shield 번호
        Assert.Equal(CombatTimings.DefaultAttackDurationSeconds, seconds);
    }
}
