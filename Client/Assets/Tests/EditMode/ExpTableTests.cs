using Incheol.Utils;
using NUnit.Framework;

namespace Incheol.Tests
{
    // 서버 Shared/ExpTable.cs와 같은 값이어야 한다(서버 쪽 ClientFormulaParityTests가 이 파일의 원본을 링크해 검증).
    public class ExpTableTests
    {
        [TestCase(1, 100)]
        [TestCase(2, 283)]
        [TestCase(4, 800)]
        [TestCase(9, 2700)]
        [TestCase(16, 6400)]
        [TestCase(25, 12500)]
        public void GetRequiredExp_FollowsFormula(int level, int expected)
        {
            Assert.AreEqual(expected, ExpTable.GetRequiredExp(level));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(ExpTable.MaxLevel)]
        [TestCase(ExpTable.MaxLevel + 1)]
        public void GetRequiredExp_OutOfRangeOrMaxLevel_IsZero(int level)
        {
            Assert.AreEqual(0, ExpTable.GetRequiredExp(level));
        }

        [Test]
        public void GetRequiredExp_LastLevelBeforeMax_IsPositive()
        {
            Assert.Greater(ExpTable.GetRequiredExp(ExpTable.MaxLevel - 1), 0);
        }

        [Test]
        public void GetRequiredExp_StrictlyIncreasesUntilMaxLevel()
        {
            for (int level = 2; level < ExpTable.MaxLevel; level++)
            {
                Assert.Greater(ExpTable.GetRequiredExp(level), ExpTable.GetRequiredExp(level - 1), $"level {level}");
            }
        }
    }
}
