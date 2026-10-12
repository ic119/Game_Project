using Incheol.Utils;
using NUnit.Framework;

namespace Incheol.Tests
{
    // 서버 Shared/StatGrowth.cs와 같은 값이어야 한다(서버 쪽은 ClientFormulaParityTests가 이 파일의 원본을 링크해 검증).
    public class StatGrowthTests
    {
        [TestCase(-5, 0)]
        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 0)]
        [TestCase(3, 1)]
        [TestCase(4, 1)]
        [TestCase(5, 2)]
        [TestCase(10, 4)]
        [TestCase(28, 13)]
        [TestCase(30, 14)]
        public void BonusAtLevel_ReturnsExpectedValue(int level, int expected)
        {
            Assert.AreEqual(expected, StatGrowth.BonusAtLevel(level));
        }

        [Test]
        public void BonusAtLevel_NeverDecreasesAsLevelRises()
        {
            int previous = StatGrowth.BonusAtLevel(1);

            for (int level = 2; level <= 100; level++)
            {
                int current = StatGrowth.BonusAtLevel(level);
                Assert.GreaterOrEqual(current, previous, $"level {level}");
                previous = current;
            }
        }
    }
}
