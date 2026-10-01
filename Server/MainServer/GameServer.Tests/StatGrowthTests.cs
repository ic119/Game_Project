using Shared;

namespace GameServer.Tests;

// 레벨업 능력치 성장 규칙(2레벨마다 모든 능력치 +1)의 기대값을 고정해두는 회귀 테스트. 클라이언트
// Assets/@Scripts/Utils/StatGrowth.cs가 같은 공식을 복제해 갖고 있으므로, 이 값을 바꾸면 클라이언트와 몬스터 밸런싱
// (Server/몬스터_밸런싱_공식.txt)도 함께 확인해야 한다.
public class StatGrowthTests
{
    [Theory]
    [InlineData(-5, 0)] // 잘못된 값은 보너스 없음
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]  // 첫 레벨업(2레벨)에는 아직 없고
    [InlineData(3, 1)]  // 3레벨부터 +1
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 4)]
    [InlineData(16, 7)]
    [InlineData(22, 10)]
    [InlineData(28, 13)]
    [InlineData(30, 14)] // 만렙(ExpTable.MaxLevel)
    public void BonusAtLevel_GrowsByOneEveryTwoLevels(int level, int expectedBonus)
    {
        Assert.Equal(expectedBonus, StatGrowth.BonusAtLevel(level));
    }

    [Fact]
    public void BonusAtLevel_NeverDecreasesAsLevelRises()
    {
        int previous = 0;
        for (int level = 1; level <= ExpTable.MaxLevel; level++)
        {
            int bonus = StatGrowth.BonusAtLevel(level);
            Assert.True(bonus >= previous, $"레벨 {level}에서 보너스가 줄었다.");
            previous = bonus;
        }
    }
}
