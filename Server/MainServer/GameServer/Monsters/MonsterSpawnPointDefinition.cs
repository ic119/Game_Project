namespace GameServer.Monsters
{
    // 맵 하나에 여러 스폰 포인트가 있을 수 있고, 각 포인트는 서로 독립된 개체수/리스폰 시간을 가진다.
    // 좌표의 단일 출처(source of truth)는 여기(서버)다 - 클라이언트 맵 프리팹에 같은 이름의 마커를 둘 수는 있지만,
    // 실제 스폰 판단과 좌표는 이 정의만 신뢰한다(두 곳에서 좌표를 관리하면 서로 어긋날 수 있기 때문).
    // 몬스터의 스탯과 AI 튜닝 값은 포인트가 아니라 타입(MonsterDefinitionCatalog)에 속한다.
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

        // 스폰할 때마다 포인트 중심에서 이 반경(m) 안의 무작위 위치로 흩뿌린다(여러 마리가 한 좌표에 겹쳐 뭉치지 않게). 0이면 항상 포인트
        // 좌표 그대로 스폰한다 - 정확한 자리가 필요한 단일 몬스터(보스 등)와 위치를 결정적으로 만들어야 하는 테스트용이다.
        public float SpawnJitterRadius { get; init; } = DefaultSpawnJitterRadius;

        public const float DefaultSpawnJitterRadius = 1.5f;

        // 이 포인트에서 나올 수 있는 몬스터 타입들. 스폰/리스폰마다 Weight 비율로 이 중 하나를 골라 그 타입의
        // 정의(MonsterDefinitionCatalog)를 그대로 적용한다(GameRoom.SpawnMonsterAtPoint). 최소 1개 이상이어야 하며,
        // MonsterSpawnCatalog.EnsureLoaded()가 로드 시점에 이를 검증해 비어있으면 서버 시작을 막는다.
        public List<MonsterSpawnEntry> Entries { get; init; } = new();

        // 몬스터가 활동할 수 있는 영역(예: 던전 방 하나). 없거나 크기가 0이면 제한 없음(필드). SpawnArea 주석 참고.
        public SpawnArea? Area { get; init; }

        // 영역이 없으면 어디든 허용한다. AI가 감지/추적/이동 제한에 쓴다.
        public bool AllowsPosition(float x, float z) => Area is not { IsDefined: true } area || area.Contains(x, z);

        // 영역이 있으면 영역 안으로 끌어온 좌표를, 없으면 그대로를 돌려준다.
        public (float X, float Z) ConstrainToArea(float x, float z) => Area is { IsDefined: true } area ? area.Clamp(x, z) : (x, z);
    }
}
