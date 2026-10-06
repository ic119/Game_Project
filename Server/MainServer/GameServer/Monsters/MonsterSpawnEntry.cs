namespace GameServer.Monsters
{
    // 스폰 포인트 하나가 여러 몬스터 타입을 섞어 낼 수 있게, 낼 타입과 그 가중치를 담는 항목.
    // 스폰/리스폰마다 소속 포인트(MonsterSpawnPointDefinition.Entries)에서 Weight 비율로 이 중 하나가
    // 선택된다(GameRoom.SpawnMonsterAtPoint 참고). 스탯/AI 값은 여기가 아니라 MonsterDefinitionCatalog가 들고 있다.
    public class MonsterSpawnEntry
    {
        public string MonsterType { get; init; } = string.Empty;

        // 선택 가중치(상대 비율). 모든 항목이 1이면 균등 랜덤이고, 3과 1이면 75%/25%다. 1 이상이어야 한다.
        public int Weight { get; init; } = 1;
    }
}
