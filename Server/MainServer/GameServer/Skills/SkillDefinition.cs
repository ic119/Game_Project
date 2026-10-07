namespace GameServer.Skills
{
    public enum SkillShape
    {
        // 시전자 앞쪽으로 Length만큼 뻗는 폭 Width의 직선 범위.
        Line,

        // 시전자 앞 ForwardOffset 지점(0이면 시전자 자신)을 중심으로 한 반지름 Radius의 원.
        Circle,

        // 시전자 앞쪽 부채꼴: 반지름 Length, 전체 각도 Angle(도).
        Cone
    }

    // 액티브 스킬 하나의 설계 데이터(Skills/SkillDefinitions.json). 서버가 권위로 판정하므로 클라이언트 값은 연출용일 뿐이다.
    // 클라이언트 스킬 UI/연출 값(아이콘, 이펙트 등)은 클라이언트가 같은 id로 따로 갖는다.
    public class SkillDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;

        // WeaponKind 이름("OneHanded" 등). 이 종류의 무기를 들고 있어야 쓸 수 있다.
        public string WeaponType { get; init; } = string.Empty;

        // 숫자 키 1~4. 슬롯별 해금 레벨은 SkillCatalog.UnlockLevelBySlot과 반드시 같아야 한다(로드 시 검증).
        public int Slot { get; init; }
        public int UnlockLevel { get; init; }

        public int ManaCost { get; init; }
        public float CooldownSeconds { get; init; }

        // 시전 모션 동안 다른 스킬을 쓸 수 없는 시간(초). 클라이언트의 이동/공격 잠금 시간과 맞춘다.
        public float CastLockSeconds { get; init; }

        public string Shape { get; init; } = string.Empty;
        public float Length { get; init; }
        public float Width { get; init; }
        public float Radius { get; init; }
        public float Angle { get; init; }
        public float ForwardOffset { get; init; }

        // 한 번의 타격이 주는 피해 = 공격력 x 배율에서 몬스터 방어력을 뺀 값(최소 1).
        public float DamageMultiplier { get; init; }

        // 연타 수와 연타 간격. 첫 타격은 HitDelaySeconds 뒤에 일어난다.
        public int Hits { get; init; } = 1;
        public float HitIntervalSeconds { get; init; }
        public float HitDelaySeconds { get; init; }

        // 한 번의 타격에 맞는 최대 몬스터 수(가까운 순).
        public int MaxTargets { get; init; } = 8;

        public SkillShape ShapeKind => Enum.Parse<SkillShape>(Shape, ignoreCase: true);
    }
}
