namespace GameServer.Monsters
{
    // 맵 하나에 여러 스폰 포인트가 있을 수 있고, 각 포인트는 서로 독립된 몬스터 종류/최대 개체수/리스폰 시간을 가진다.
    // 좌표의 단일 출처(source of truth)는 여기(서버)다 - 클라이언트 맵 프리팹에 같은 이름의 마커를 둘 수는 있지만,
    // 실제 스폰 판단과 좌표는 이 정의만 신뢰한다(두 곳에서 좌표를 관리하면 서로 어긋날 수 있기 때문).
    public class MonsterSpawnPointDefinition
    {
        public string PointId { get; init; } = string.Empty;
        public string MonsterType { get; init; } = string.Empty;
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float RotationY { get; init; }

        // 이 포인트가 동시에 유지할 최대 개체 수. 몬스터가 죽어 이 수 밑으로 떨어지면 RespawnSeconds 후 보충된다.
        public int MaxAlive { get; init; } = 1;
        public float RespawnSeconds { get; init; } = 20f;

        // 몬스터 스탯도 여기(서버)가 유일한 출처다 - 플레이어와 달리 몬스터는 소유 클라이언트/DB가 없다.
        public int MaxHp { get; init; } = 30;
        public int AttackPower { get; init; } = 5;
        public int Defense { get; init; } = 0;
    }
}
