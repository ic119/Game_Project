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
    [InlineData(10, 5, 190)] // 100 + 10*5 + (5-1)*10
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
