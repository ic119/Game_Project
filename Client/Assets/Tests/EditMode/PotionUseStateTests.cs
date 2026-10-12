using Incheol.Presenter.Scene;
using NUnit.Framework;

namespace Incheol.Tests
{
    // RemainingSeconds는 Time.unscaledTime에 의존하므로(비결정적) 경계값과 방향성 위주로만 검증한다.
    public class PotionUseStateTests
    {
        private PotionUseState state;

        [SetUp]
        public void CreateState()
        {
            state = new PotionUseState();
        }

        [Test]
        public void NewState_IsIdle()
        {
            Assert.IsFalse(state.IsPending);
            Assert.AreEqual(0f, state.RemainingSeconds);
        }

        [Test]
        public void SetCooldownMs_LeavesRemainingTimeUpToThatLong()
        {
            state.SetCooldownMs(5000);

            Assert.Greater(state.RemainingSeconds, 4f);
            Assert.LessOrEqual(state.RemainingSeconds, 5f);
        }

        [Test]
        public void SetCooldownMs_Zero_MeansNoWait()
        {
            state.SetCooldownMs(5000);
            state.SetCooldownMs(0);

            Assert.AreEqual(0f, state.RemainingSeconds);
        }

        [Test]
        public void SetCooldownMs_Negative_NeverGoesBelowZero()
        {
            state.SetCooldownMs(-1000);

            Assert.AreEqual(0f, state.RemainingSeconds);
        }

        [Test]
        public void Reset_ClearsPendingAndCooldown()
        {
            state.IsPending = true;
            state.SetCooldownMs(5000);

            state.Reset();

            Assert.IsFalse(state.IsPending);
            Assert.AreEqual(0f, state.RemainingSeconds);
        }
    }
}
