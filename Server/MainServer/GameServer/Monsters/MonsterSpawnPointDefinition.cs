namespace GameServer.Monsters
{
    // 맵 하나에 여러 스폰 포인트가 있을 수 있고, 각 포인트는 서로 독립된 개체수/리스폰 시간/AI 튜닝을 가진다.
    // 좌표의 단일 출처(source of truth)는 여기(서버)다 - 클라이언트 맵 프리팹에 같은 이름의 마커를 둘 수는 있지만,
    // 실제 스폰 판단과 좌표는 이 정의만 신뢰한다(두 곳에서 좌표를 관리하면 서로 어긋날 수 있기 때문).
    public class MonsterSpawnPointDefinition
    {
        public string PointId { get; init; } = string.Empty;
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float RotationY { get; init; }

        // 이 포인트가 동시에 유지할 최대 개체 수. 몬스터가 죽어 이 수 밑으로 떨어지면 RespawnSeconds 후 보충된다.
        public int MaxAlive { get; init; } = 1;
        public float RespawnSeconds { get; init; } = 20f;

        // 이 포인트에서 나올 수 있는 몬스터 타입들. 스폰/리스폰마다 이 중 하나를 무작위로 골라 그 타입의
        // 스탯을 그대로 적용한다(GameRoom.SpawnMonsterAtPoint). 최소 1개 이상이어야 하며,
        // MonsterSpawnCatalog.EnsureLoaded()가 로드 시점에 이를 검증해 비어있으면 서버 시작을 막는다.
        public List<MonsterSpawnEntry> Entries { get; init; } = new();

        // 인식/추적 AI 튜닝 값(GameRoom.TickMonsterAiAsync 참고). 몬스터 타입별 분기 없이 이 값들만으로
        // 동작하므로, 새 몬스터를 추가할 때도 스폰 포인트에 값만 채우면 자동으로 같은 AI를 갖는다.
        // 몬스터로부터 이 거리 안에 플레이어가 들어오면 추적을 시작한다.
        public float DetectionRange { get; init; } = 6f;

        // 추적/복귀 중 초당 이동 거리.
        public float ChaseSpeed { get; init; } = 2.5f;

        // 스폰 지점으로부터 이 거리 이상 벗어나면 추적을 포기하고 복귀한다.
        public float LeashRange { get; init; } = 10f;
    }
}
