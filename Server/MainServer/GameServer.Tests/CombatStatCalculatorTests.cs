using GameServer.Combat;

namespace GameServer.Tests;

// CombatStatCalculator의 공식은 Client Assets/@Scripts/Model/Combat/CombatStatComponent.cs(ApplyFromUserStats)와
// Assets/@Scripts/Model/Combat/HealthComponent.cs(ApplyFromUserStats)에 같은 내용으로 각각 구현돼 있다
// (Unity와 ASP.NET 서버가 런타임이 달라 코드를 공유하지 못해 생긴 중복이다).
// 이 테스트는 서버 쪽 공식의 기대값을 고정해두는 회귀 테스트다 - 이 중 하나를 고치면
// 클라이언트 쪽 공식과 이 테스트의 기대값을 함께 확인해야 한다.
public class CombatStatCalculatorTests
{
    [Theory]
    [InlineData(0, 1, 100)]  // 100 + agi 0*5 + (레벨 1은 레벨 보너스 없음)
    [InlineData(10, 1, 150)] // 100 + 10*5
    [InlineData(10, 5, 200)] // 100 + (10+2)*5 + (5-1)*10  (5레벨 성장 보너스 +2가 agi에 더해진다)
    [InlineData(10, 10, 260)] // 100 + (10+4)*5 + (10-1)*10
    [InlineData(10, 28, 485)] // 100 + (10+13)*5 + (28-1)*10
    public void CalculateMaxHp_MatchesClientHealthComponentFormula(int agi, int level, int expectedMaxHp)
    {
        var snapshot = new CharacterSnapshot("tester", 0, 0, 0, level, 0, 0, agi, Array.Empty<string>());

        int actualMaxHp = CombatStatCalculator.CalculateMaxHp(snapshot);

        Assert.Equal(expectedMaxHp, actualMaxHp);
    }

    [Theory]
    [InlineData(10, 0)] // str만 있을 때 공격력 = str
    [InlineData(0, 5)]  // agi만 있을 때 방어력 = agi/2(정수 나눗셈)
    public void Calculate_BaseStats_MatchClientCombatStatComponentFormula(int str, int agi)
    {
        var snapshot = new CharacterSnapshot("tester", 0, 0, 0, 1, 0, str, agi, Array.Empty<string>());

        (int attackPower, int defense) = CombatStatCalculator.Calculate(snapshot);

        Assert.Equal(str, attackPower);
        Assert.Equal(agi / 2, defense);
    }

    [Theory]
    [InlineData(1, 10, 5)]   // 1레벨은 보너스 없음 (기본 str/agi 10 -> 공격 10, 방어 5)
    [InlineData(2, 10, 5)]   // 2레벨도 아직 보너스 없음
    [InlineData(3, 11, 5)]   // 3레벨 보너스 +1 -> 공격 11, 방어 (10+1)/2 = 5
    [InlineData(10, 14, 7)]  // 보너스 +4 -> 공격 14, 방어 (10+4)/2 = 7
    [InlineData(28, 23, 11)] // 보너스 +13 -> 공격 23, 방어 (10+13)/2 = 11
    [InlineData(30, 24, 12)] // 보너스 +14 -> 공격 24, 방어 (10+14)/2 = 12
    public void Calculate_AddsLevelGrowthToStrAndAgi(int level, int expectedAttack, int expectedDefense)
    {
        var snapshot = new CharacterSnapshot("tester", 0, 0, 0, level, 0, 10, 10, Array.Empty<string>());

        (int attackPower, int defense) = CombatStatCalculator.Calculate(snapshot);

        Assert.Equal(expectedAttack, attackPower);
        Assert.Equal(expectedDefense, defense);
    }

    [Fact]
    public void Calculate_LevelOverrideTakesPrecedenceOverSnapshotLevel()
    {
        // 접속 중 레벨업하면 서버 메모리 레벨이 DB(snapshot.Level)보다 앞선다 - 이미 오른 레벨 기준으로 계산해야 한다.
        var snapshot = new CharacterSnapshot("tester", 0, 0, 0, 1, 0, 10, 10, Array.Empty<string>());

        (int attackPower, int defense) = CombatStatCalculator.Calculate(snapshot, level: 10);
        int maxHp = CombatStatCalculator.CalculateMaxHp(snapshot, level: 10);

        Assert.Equal(14, attackPower);
        Assert.Equal(7, defense);
        Assert.Equal(260, maxHp);
    }

    [Fact]
    public void Calculate_AddsEquippedItemBonuses()
    {
        var snapshot = new CharacterSnapshot("tester", 0, 0, 0, 1, 0, 10, 4,
            new[] { "test_weapon_regression", "test_armor_regression" });

        (int attackPower, int defense) = CombatStatCalculator.Calculate(snapshot);

        Assert.Equal(10 + 7, attackPower); // str + 무기 BonusAttackPower
        Assert.Equal(4 / 2 + 3, defense);  // agi/2 + 방어구 BonusDefense
    }

    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(100, 0, 1)]   // healPercent가 0이어도 최소 1은 회복
    [InlineData(115, 33, 38)] // 반올림 처리 확인 (37.95 -> 38)
    public void CalculateHealAmount_NeverBelowOne(int maxHp, int healPercent, int expectedHeal)
    {
        int actualHeal = CombatStatCalculator.CalculateHealAmount(maxHp, healPercent);

        Assert.Equal(expectedHeal, actualHeal);
    }

    [Theory]
    [InlineData(10, 5, 5)]
    [InlineData(10, 100, 1)] // 방어력이 아무리 높아도 최소 1은 들어간다
    public void ApplyDefense_NeverBelowOne(int rawDamage, int defense, int expectedDamage)
    {
        int actualDamage = CombatStatCalculator.ApplyDefense(rawDamage, defense);

        Assert.Equal(expectedDamage, actualDamage);
    }
}
