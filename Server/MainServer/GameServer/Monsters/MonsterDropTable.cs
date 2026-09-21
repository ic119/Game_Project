namespace GameServer.Monsters
{
    // 몬스터 타입 하나의 처치 보상 정의. MonsterSpawnEntry(전투 스탯)와 달리 스폰 포인트가 아니라
    // MonsterType에 직접 묶인다 - 같은 타입이 여러 맵/포인트에 재사용돼도 드롭 테이블은 하나만 정의하면 된다.
    public class MonsterDropTable
    {
        public int MinGold { get; init; }
        public int MaxGold { get; init; }
        public List<DropTableEntry> Items { get; init; } = new();
    }
}
