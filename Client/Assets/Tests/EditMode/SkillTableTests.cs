using System.Collections.Generic;
using System.Linq;
using Incheol.Utils;
using NUnit.Framework;

namespace Incheol.Tests
{
    // 서버 SkillDefinitions.json과의 일치는 서버 SkillTableParityTests가 검사한다. 여기서는 표 자체의 구조 규칙과 대표 값을 고정한다.
    public class SkillTableTests
    {
        private static readonly int[] WeaponsWithSkills = { 1, 2, 4, 5 };

        [Test]
        public void SlotUnlockLevels_AreThreeSixNineTwelve()
        {
            Assert.AreEqual(SkillTable.SlotCount + 1, SkillTable.SlotUnlockLevels.Length);
            CollectionAssert.AreEqual(new[] { 0, 3, 6, 9, 12 }, SkillTable.SlotUnlockLevels);
        }

        [Test]
        public void EveryWeaponWithSkills_HasAllFourSlots()
        {
            foreach (int weapon in WeaponsWithSkills)
            {
                for (int slot = 1; slot <= SkillTable.SlotCount; slot++)
                {
                    Assert.IsTrue(SkillTable.TryGet(weapon, slot, out _), $"weapon {weapon} slot {slot}");
                }
            }

            Assert.AreEqual(WeaponsWithSkills.Length * SkillTable.SlotCount, SkillTable.All.Count);
        }

        [Test]
        public void EntryUnlockLevel_MatchesSlotUnlockLevels()
        {
            foreach (SkillTable.Entry entry in SkillTable.All)
            {
                Assert.AreEqual(SkillTable.SlotUnlockLevels[entry.Slot], entry.UnlockLevel, entry.Id);
            }
        }

        [Test]
        public void EntryIds_AreUnique()
        {
            List<string> ids = SkillTable.All.Select(e => e.Id).ToList();

            CollectionAssert.AllItemsAreUnique(ids);
        }

        [Test]
        public void EntryValues_ArePositiveAndConsistent()
        {
            foreach (SkillTable.Entry entry in SkillTable.All)
            {
                Assert.Greater(entry.ManaCost, 0, entry.Id);
                Assert.Greater(entry.CooldownSeconds, 0f, entry.Id);
                Assert.Greater(entry.CastLockSeconds, 0f, entry.Id);
                Assert.GreaterOrEqual(entry.Area.Hits, 1, entry.Id);
                Assert.Greater(entry.AnimatorSkillIndex, 0, entry.Id);
                Assert.Greater(entry.Area.EffectSize, 0f, entry.Id);
            }
        }

        [Test]
        public void HigherSlot_CostsMoreManaAndCooldown_WithinSameWeapon()
        {
            foreach (int weapon in WeaponsWithSkills)
            {
                for (int slot = 2; slot <= SkillTable.SlotCount; slot++)
                {
                    SkillTable.TryGet(weapon, slot - 1, out var lower);
                    SkillTable.TryGet(weapon, slot, out var higher);

                    Assert.Greater(higher.ManaCost, lower.ManaCost, $"weapon {weapon} slot {slot} mana");
                    Assert.Greater(higher.CooldownSeconds, lower.CooldownSeconds, $"weapon {weapon} slot {slot} cooldown");
                }
            }
        }

        [Test]
        public void TryGet_KnownEntry_ReturnsExpectedValues()
        {
            bool ok = SkillTable.TryGet(1, 1, out SkillTable.Entry entry);

            Assert.IsTrue(ok);
            Assert.AreEqual("ohs_pierce", entry.Id);
            Assert.AreEqual(3, entry.UnlockLevel);
            Assert.AreEqual(8, entry.ManaCost);
            Assert.AreEqual(5f, entry.CooldownSeconds);
            Assert.AreEqual(SkillTable.AreaShape.Line, entry.Area.Shape);
        }

        [TestCase(0, 1)]   // 맨손은 슬롯 스킬 없음
        [TestCase(1, 0)]
        [TestCase(1, 5)]
        [TestCase(99, 1)]
        public void TryGet_UnknownCombination_ReturnsFalse(int weapon, int slot)
        {
            Assert.IsFalse(SkillTable.TryGet(weapon, slot, out _));
        }

        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        [TestCase(0, false)]
        [TestCase(3, false)]
        public void HasSkills_MatchesWeaponsInTable(int weapon, bool expected)
        {
            Assert.AreEqual(expected, SkillTable.HasSkills(weapon));
        }

        [Test]
        public void CenterForwardDistance_UsesOffsetForCircleAndHalfLengthOtherwise()
        {
            SkillTable.TryGet(1, 3, out var circle); // 도약 내려찍기: Circle(2.5, offset 3)
            SkillTable.TryGet(1, 1, out var line);   // 돌진 찌르기: Line(3.5)

            Assert.AreEqual(3f, circle.Area.CenterForwardDistance);
            Assert.AreEqual(1.75f, line.Area.CenterForwardDistance);
            Assert.AreEqual(5f, circle.Area.EffectSize);
            Assert.AreEqual(3.5f, line.Area.EffectSize);
        }
    }
}
