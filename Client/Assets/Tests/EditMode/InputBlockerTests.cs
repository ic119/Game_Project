using Incheol.Utils;
using NUnit.Framework;

namespace Incheol.Tests
{
    public class InputBlockerTests
    {
        private readonly object ownerA = new();
        private readonly object ownerB = new();

        // 정적 상태라 테스트끼리 새지 않게 매번 풀어 둔다.
        [SetUp]
        [TearDown]
        public void ReleaseAll()
        {
            InputBlocker.SetBlocked(ownerA, false);
            InputBlocker.SetBlocked(ownerB, false);
        }

        [Test]
        public void IsBlocked_IsFalse_WhenNobodyBlocks()
        {
            Assert.IsFalse(InputBlocker.IsBlocked);
        }

        [Test]
        public void SetBlocked_True_BlocksUntilReleased()
        {
            InputBlocker.SetBlocked(ownerA, true);
            Assert.IsTrue(InputBlocker.IsBlocked);

            InputBlocker.SetBlocked(ownerA, false);
            Assert.IsFalse(InputBlocker.IsBlocked);
        }

        [Test]
        public void SameOwnerBlockingTwice_CountsOnce()
        {
            InputBlocker.SetBlocked(ownerA, true);
            InputBlocker.SetBlocked(ownerA, true);

            InputBlocker.SetBlocked(ownerA, false);

            Assert.IsFalse(InputBlocker.IsBlocked);
        }

        [Test]
        public void StaysBlocked_UntilEveryOwnerReleases()
        {
            InputBlocker.SetBlocked(ownerA, true);
            InputBlocker.SetBlocked(ownerB, true);

            InputBlocker.SetBlocked(ownerA, false);
            Assert.IsTrue(InputBlocker.IsBlocked);

            InputBlocker.SetBlocked(ownerB, false);
            Assert.IsFalse(InputBlocker.IsBlocked);
        }

        [Test]
        public void ReleasingOwnerThatNeverBlocked_IsHarmless()
        {
            InputBlocker.SetBlocked(ownerA, true);

            InputBlocker.SetBlocked(ownerB, false);

            Assert.IsTrue(InputBlocker.IsBlocked);
        }

        [Test]
        public void NullOwner_IsIgnored()
        {
            InputBlocker.SetBlocked(null, true);

            Assert.IsFalse(InputBlocker.IsBlocked);
        }
    }
}
