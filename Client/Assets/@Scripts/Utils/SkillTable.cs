namespace Incheol.Utils
{
    // 서버(GameServer/Skills/SkillDefinitions.json)의 액티브 스킬 중 클라이언트가 알아야 하는 값(어떤 슬롯이 몇 레벨에 열리는지, 마나/쿨다운/시전 시간,
    // 어떤 모션을 재생하는지)의 복제본이다. 판정(범위/피해)은 서버가 권위로 하므로 여기에 두지 않는다. 값이 서버와 어긋나면 클라이언트가
    // 서버가 거부할 요청을 보내거나 모션 길이가 맞지 않게 되므로, GameServer.Tests의 SkillTableParityTests가 JSON과 같은 값인지 검사한다.
    // 슬롯 키 1~4는 해금 레벨 3/6/9/12다(SlotUnlockLevels). Unity 의존이 없어 서버 테스트 프로젝트가 그대로 링크한다.
    public static class SkillTable
    {
        public const int SlotCount = 4;

        public readonly struct Entry
        {
            public Entry(int weaponIndex, int slot, string id, string name, int unlockLevel, int manaCost, float cooldownSeconds, float castLockSeconds, int animatorSkillIndex)
            {
                WeaponIndex = weaponIndex;
                Slot = slot;
                Id = id;
                Name = name;
                UnlockLevel = unlockLevel;
                ManaCost = manaCost;
                CooldownSeconds = cooldownSeconds;
                CastLockSeconds = castLockSeconds;
                AnimatorSkillIndex = animatorSkillIndex;
            }

            // WeaponType의 정수 값(None 0, OneHanded 1, TwoHanded 2, Wand 4, Spear 5).
            public int WeaponIndex { get; }
            public int Slot { get; }
            public string Id { get; }
            public string Name { get; }
            public int UnlockLevel { get; }
            public int ManaCost { get; }
            public float CooldownSeconds { get; }

            // 시전 모션 동안 이동/공격/다른 스킬이 잠기는 시간. 서버 CastLockSeconds와 같다.
            public float CastLockSeconds { get; }

            // Attack Layer의 SkillIndex 파라미터 값. 이 값으로 해당 스킬 모션 상태로 전환한다(0 = 스킬 없음).
            public int AnimatorSkillIndex { get; }
        }

        // 슬롯 -> 해금 레벨.
        public static readonly int[] SlotUnlockLevels = { 0, 3, 6, 9, 12 };

        private static readonly Entry[] Entries =
        {
            // 한손검. SkillIndex 1(SwordPierce)/4(Wheelwind)는 기존 상태를 그대로 쓰고, 5/6은 도약 내려찍기/검기 참격으로 새로 추가했다.
            new Entry(1, 1, "ohs_pierce", "돌진 찌르기", 3, 12, 5f, 0.6f, 1),
            new Entry(1, 2, "ohs_whirlwind", "회전 베기", 6, 18, 8f, 0.7f, 4),
            new Entry(1, 3, "ohs_leap_slam", "도약 내려찍기", 9, 26, 12f, 0.95f, 5),
            new Entry(1, 4, "ohs_sword_wave", "검기 참격", 12, 36, 20f, 0.8f, 6),

            // 양손검. 모션 상태는 한손검과 같은 SkillIndex를 쓰고, 애니메이터의 무기별 블렌드 트리(WeaponIndex)가 양손검 클립을 고른다.
            new Entry(2, 1, "ths_smash", "내려치기", 3, 14, 6f, 0.55f, 1),
            new Entry(2, 2, "ths_great_spin", "대회전", 6, 20, 9f, 0.55f, 4),
            new Entry(2, 3, "ths_earth_cleave", "대지 가르기", 9, 28, 13f, 1.15f, 5),
            new Entry(2, 4, "ths_fury_strike", "분노의 일격", 12, 38, 22f, 1.05f, 6),

            // 완드. 4번(메테오)만 전용 긴 시전 상태(MeteorCast, SkillIndex 7)를 쓴다.
            new Entry(4, 1, "wand_fireball", "파이어볼", 3, 12, 5f, 0.6f, 1),
            new Entry(4, 2, "wand_frost_nova", "프로스트 노바", 6, 18, 8f, 0.7f, 4),
            new Entry(4, 3, "wand_lightning", "낙뢰", 9, 26, 12f, 1.35f, 5),
            new Entry(4, 4, "wand_meteor", "메테오", 12, 38, 22f, 1.45f, 7),

            // 창.
            new Entry(5, 1, "spear_flurry", "연속 찌르기", 3, 12, 5f, 0.6f, 1),
            new Entry(5, 2, "spear_charge", "돌진", 6, 18, 8f, 0.7f, 4),
            new Entry(5, 3, "spear_throw", "투창", 9, 26, 12f, 1.05f, 5),
            new Entry(5, 4, "spear_ascension", "승천창", 12, 36, 20f, 0.8f, 6),
        };

        public static System.Collections.Generic.IReadOnlyList<Entry> All => Entries;

        public static bool TryGet(int weaponIndex, int slot, out Entry entry)
        {
            foreach (Entry candidate in Entries)
            {
                if (candidate.WeaponIndex == weaponIndex && candidate.Slot == slot)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        // 이 무기에 슬롯 스킬이 하나라도 있는지(스킬 UI를 보일지 정하는 데 쓴다).
        public static bool HasSkills(int weaponIndex)
        {
            foreach (Entry candidate in Entries)
            {
                if (candidate.WeaponIndex == weaponIndex)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
