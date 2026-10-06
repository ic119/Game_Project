namespace GameServer.Monsters
{
    // 보스가 쓰는 스킬 종류. 값은 S2CBossSkillTelegraphBroadcast/S2CBossSkillEndBroadcast의 SkillType과 같다(클라이언트 연출이 이 값으로 갈린다).
    public enum BossSkillType : byte
    {
        // 보스 주변 원형 범위를 예고한 뒤 그 안의 플레이어를 친다.
        AreaSlam = 1,

        // 예고된 직선 방향으로 보스가 빠르게 달려가며 경로 위의 플레이어를 친다. 끝난 뒤 잠시 멈춘다(공략 기회).
        Charge = 2,

        // 예고된 자리에 하수인을 소환한다.
        Summon = 3
    }

    // 스킬 하나의 수치. Monsters/MonsterDefinitions.json의 보스 정의(bossPattern.skills)에서 이름(키)으로 참조된다.
    // 스킬 종류별로 쓰는 필드가 다르며, 쓰지 않는 필드는 무시된다(MonsterDefinitionCatalog가 종류별 필수 값을 검증한다).
    public class BossSkillDefinition
    {
        public BossSkillType Type { get; init; }

        // 예고 시간(초). 시전 시작부터 판정까지이며, 이 시간 동안 플레이어는 범위 밖으로 벗어나거나 대쉬로 피할 수 있다.
        public float TelegraphSeconds { get; init; } = 1f;

        // 발동한 뒤 같은 스킬을 다시 쓸 수 있기까지의 시간(초).
        public float CooldownSeconds { get; init; } = 8f;

        // 발동(또는 돌진이 끝난) 뒤 보스가 제자리에서 멈춰 있는 시간(초). 이 동안 일반 공격도 하지 않는다.
        public float RecoverSeconds { get; init; } = 1f;

        // 피해 = 보스 공격력 x 이 배율(방어력 적용 전). 스킬 명중은 일반 공격보다 강하다.
        public float DamageMultiplier { get; init; } = 1.5f;

        // --- AreaSlam ---
        // 원형 범위 반지름(m). 보스 위치를 중심으로 한다.
        public float Radius { get; init; }

        // 대상이 이 거리(m) 안에 있을 때만 시전한다.
        public float TriggerRange { get; init; }

        // --- Charge ---
        // 돌진 경로의 폭(m)과 길이(m), 돌진 속도(m/s).
        public float Width { get; init; }
        public float Length { get; init; }
        public float Speed { get; init; }

        // 대상까지의 거리가 [MinRange, MaxRange](m) 안일 때만 시전한다. 너무 가까우면 돌진이 의미가 없고, 너무 멀면 피하기 쉽다.
        public float MinRange { get; init; }
        public float MaxRange { get; init; }

        // --- Summon ---
        // 소환할 몬스터 종류(MonsterDefinitions.json의 키), 한 번에 소환하는 수, 이 보스가 동시에 거느릴 수 있는 최대 수.
        public string SummonMonsterType { get; init; } = string.Empty;
        public int SummonCount { get; init; }
        public int MaxAlive { get; init; }

        // 하수인이 보스로부터 이 반경(m) 안의 자리에 나타난다.
        public float SummonRadius { get; init; }
    }

    // 체력 구간(페이즈) 하나. 보스 체력이 BelowHpPercent% 이하가 되면 이 구간이 적용되고, 그 구간에서 쓸 스킬(이름)과 스킬 사이 간격을 정한다.
    public class BossPhaseDefinition
    {
        // 체력이 이 비율(%) 이하일 때 적용된다. 가장 처음 구간은 100이다.
        public int BelowHpPercent { get; init; } = 100;

        // 한 스킬이 끝난 뒤 다음 스킬을 고르기까지 쉬는 시간(초). 뒤 구간일수록 짧게 해 점점 몰아치게 한다.
        public float SkillIntervalSeconds { get; init; } = 4f;

        // 이 구간에서 쓸 수 있는 스킬 이름들(BossPatternDefinition.Skills의 키). 조건을 만족하는 것 중 무작위로 고른다.
        public List<string> Skills { get; init; } = new();
    }

    // 보스 하나의 패턴 전체: 스킬 수치와 체력 구간별 사용 스킬.
    public class BossPatternDefinition
    {
        public Dictionary<string, BossSkillDefinition> Skills { get; init; } = new();

        // BelowHpPercent 내림차순(처음이 100)으로 정렬돼 있어야 한다 - MonsterDefinitionCatalog가 로드할 때 정렬/검증한다.
        public List<BossPhaseDefinition> Phases { get; init; } = new();
    }
}
