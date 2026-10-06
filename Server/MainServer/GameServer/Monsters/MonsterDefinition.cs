namespace GameServer.Monsters
{
    // 몬스터 타입 하나의 전투 스탯과 인식/추적 AI 튜닝 값. Monsters/MonsterDefinitions.json에서 MonsterType을 키로
    // 읽어온다(MonsterDefinitionCatalog). 같은 타입이 여러 스폰 포인트/맵에 나와도 값을 한 곳에서만 고치면 되고,
    // 스폰 포인트는 "어느 타입을 낼지"(MonsterSpawnEntry)만 안다. 몬스터는 소유 클라이언트/DB가 없어
    // 스탯의 유일한 출처가 여기(서버)다.
    public class MonsterDefinition
    {
        public int MaxHp { get; init; } = 30;
        public int AttackPower { get; init; } = 5;
        public int Defense { get; init; } = 0;

        // 처치 시 지급할 경험치. 가이드라인: round(MaxHp * 0.4 + AttackPower * 2).
        public int ExpReward { get; init; } = 20;

        // 인식/추적 AI 튜닝 값(GameRoom.TickMonsterAiAsync 참고). 몬스터 타입별 분기 없이 이 값들만으로
        // 동작하므로, 새 몬스터를 추가할 때도 여기에 값만 채우면 자동으로 같은 AI를 갖는다.
        // 몬스터로부터 이 거리 안에 플레이어가 들어오면 추적을 시작한다.
        public float DetectionRange { get; init; } = 6f;

        // 추적/복귀 중 초당 이동 거리.
        public float ChaseSpeed { get; init; } = 2.5f;

        // 스폰 지점으로부터 이 거리 이상 벗어나면 추적을 포기하고 복귀한다.
        public float LeashRange { get; init; } = 10f;

        // 몸통 반경(m). 이동 격자(NavGrid)가 있는 맵에서 장애물을 이만큼 부풀려 경로를 찾아, 덩치 큰 몬스터가
        // 기둥/가구 모서리에 끼지 않고 지나가게 한다. 격자가 없는 맵(필드)에서는 쓰이지 않는다.
        public float AgentRadius { get; init; } = 0.5f;

        // 보스 전용 패턴(체력 구간별 범위 공격/돌진/소환). null이면 일반 몬스터라 기본 근접 공격만 한다.
        public BossPatternDefinition? BossPattern { get; init; }
    }
}
