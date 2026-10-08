namespace Incheol.Utils
{
    // 서버(GameServer/Skills/SkillDefinitions.json)의 액티브 스킬 중 클라이언트가 알아야 하는 값(어떤 슬롯이 몇 레벨에 열리는지, 마나/쿨다운/시전 시간,
    // 어떤 모션을 재생하는지, 이펙트를 어디에 언제 터뜨릴지 알려 주는 범위 형태/타격 시각)의 복제본이다. 판정(범위 안에 누가 있는지/피해)은 서버가
    // 권위로 하므로 Area는 순수한 연출용이다. 값이 서버와 어긋나면 클라이언트가 서버가 거부할 요청을 보내거나 모션 길이/이펙트 위치가 맞지 않게 되므로,
    // GameServer.Tests의 SkillTableParityTests가 JSON과 같은 값인지 검사한다.
    // 슬롯 키 1~4는 해금 레벨 3/6/9/12다(SlotUnlockLevels). Unity 의존이 없어 서버 테스트 프로젝트가 그대로 링크한다.
    public static class SkillTable
    {
        public const int SlotCount = 4;

        // 서버 GameServer.Skills.SkillShape와 같은 이름/의미.
        public enum AreaShape
        {
            // 시전자 앞쪽으로 Length만큼 뻗는 폭 Width의 직선.
            Line,

            // 시전자 앞 ForwardOffset 지점(0이면 자신)을 중심으로 한 반지름 Radius의 원.
            Circle,

            // 시전자 앞쪽 부채꼴: 반지름 Length, 전체 각도 Angle(도).
            Cone
        }

        // 스킬이 맞히는 범위와 타격 시각. 서버 SkillDefinition의 같은 이름 값과 같다.
        public readonly struct Area
        {
            public Area(AreaShape shape, float length, float width, float radius, float angle, float forwardOffset, float hitDelaySeconds, int hits, float hitIntervalSeconds)
            {
                Shape = shape;
                Length = length;
                Width = width;
                Radius = radius;
                Angle = angle;
                ForwardOffset = forwardOffset;
                HitDelaySeconds = hitDelaySeconds;
                Hits = hits;
                HitIntervalSeconds = hitIntervalSeconds;
            }

            public AreaShape Shape { get; }
            public float Length { get; }
            public float Width { get; }
            public float Radius { get; }
            public float Angle { get; }
            public float ForwardOffset { get; }
            public float HitDelaySeconds { get; }
            public int Hits { get; }
            public float HitIntervalSeconds { get; }

            /// <summary>
            /// 범위의 중심이 시전자 앞쪽으로 얼마나 떨어져 있는지(m). 직선/부채꼴은 길이의 절반, 원은 ForwardOffset이다.
            /// 이펙트를 범위 한가운데에 놓는 기준이다.
            /// </summary>
            public float CenterForwardDistance => Shape == AreaShape.Circle ? ForwardOffset : Length * 0.5f;

            /// <summary>
            /// 이펙트 크기를 범위에 맞출 때 쓰는 기준 길이(m). 직선/부채꼴은 길이, 원은 지름이다.
            /// </summary>
            public float EffectSize => Shape == AreaShape.Circle ? Radius * 2f : Length;
        }

        public readonly struct Entry
        {
            public Entry(int weaponIndex, int slot, string id, string name, int unlockLevel, int manaCost, float cooldownSeconds, float castLockSeconds, int animatorSkillIndex, Area area)
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
                Area = area;
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

            // 범위 형태와 타격 시각(이펙트 배치용).
            public Area Area { get; }
        }

        // 슬롯 -> 해금 레벨.
        public static readonly int[] SlotUnlockLevels = { 0, 3, 6, 9, 12 };

        private static Area Line(float length, float width, float hitDelay, int hits = 1, float hitInterval = 0f)
            => new Area(AreaShape.Line, length, width, 0f, 0f, 0f, hitDelay, hits, hitInterval);

        private static Area Circle(float radius, float forwardOffset, float hitDelay)
            => new Area(AreaShape.Circle, 0f, 0f, radius, 0f, forwardOffset, hitDelay, 1, 0f);

        private static Area Cone(float length, float angle, float hitDelay)
            => new Area(AreaShape.Cone, length, 0f, 0f, angle, 0f, hitDelay, 1, 0f);

        private static readonly Entry[] Entries =
        {
            // 한손검. SkillIndex 1(SwordPierce)/4(Wheelwind)는 기존 상태를 그대로 쓰고, 5/6은 도약 내려찍기/검기 참격으로 새로 추가했다.
            new Entry(1, 1, "ohs_pierce", "돌진 찌르기", 3, 8, 5f, 0.6f, 1, Line(3.5f, 1.2f, 0.3f)),
            new Entry(1, 2, "ohs_whirlwind", "회전 베기", 6, 13, 8f, 0.7f, 4, Circle(2.5f, 0f, 0.3f)),
            new Entry(1, 3, "ohs_leap_slam", "도약 내려찍기", 9, 18, 12f, 0.95f, 5, Circle(2.5f, 3f, 0.5f)),
            new Entry(1, 4, "ohs_sword_wave", "검기 참격", 12, 25, 20f, 0.8f, 6, Line(6f, 1.5f, 0.45f)),

            // 양손검. 모션 상태는 한손검과 같은 SkillIndex를 쓰고, 애니메이터의 무기별 블렌드 트리(WeaponIndex)가 양손검 클립을 고른다.
            new Entry(2, 1, "ths_smash", "내려치기", 3, 10, 6f, 0.55f, 1, Cone(2.5f, 120f, 0.25f)),
            new Entry(2, 2, "ths_great_spin", "대회전", 6, 14, 9f, 0.55f, 4, Circle(3f, 0f, 0.4f)),
            new Entry(2, 3, "ths_earth_cleave", "대지 가르기", 9, 20, 13f, 1.15f, 5, Line(5f, 2f, 0.8f)),
            new Entry(2, 4, "ths_fury_strike", "분노의 일격", 12, 27, 22f, 1.05f, 6, Circle(4f, 3f, 1.0f)),

            // 완드. 4번(메테오)만 전용 긴 시전 상태(MeteorCast, SkillIndex 7)를 쓴다.
            new Entry(4, 1, "wand_fireball", "파이어볼", 3, 8, 5f, 0.6f, 1, Line(6f, 1f, 0.3f)),
            new Entry(4, 2, "wand_frost_nova", "프로스트 노바", 6, 13, 8f, 0.7f, 4, Circle(3f, 0f, 0.2f)),
            new Entry(4, 3, "wand_lightning", "낙뢰", 9, 18, 12f, 1.35f, 5, Circle(2.5f, 6f, 1.0f)),
            new Entry(4, 4, "wand_meteor", "메테오", 12, 27, 22f, 1.45f, 7, Circle(4f, 7f, 1.4f)),

            // 창.
            new Entry(5, 1, "spear_flurry", "연속 찌르기", 3, 8, 5f, 0.6f, 1, Line(3.5f, 1f, 0.25f, hits: 3, hitInterval: 0.1f)),
            new Entry(5, 2, "spear_charge", "돌진", 6, 13, 8f, 0.7f, 4, Line(5f, 1.2f, 0.25f)),
            new Entry(5, 3, "spear_throw", "투창", 9, 18, 12f, 1.05f, 5, Line(8f, 1f, 0.45f)),
            new Entry(5, 4, "spear_ascension", "승천창", 12, 25, 20f, 0.8f, 6, Line(7f, 2f, 0.45f)),
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
