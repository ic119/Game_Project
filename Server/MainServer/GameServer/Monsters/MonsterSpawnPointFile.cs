namespace GameServer.Monsters
{
    // SpawnPoints/{mapId}.json 파일 하나의 최상위 구조. 파일명(확장자 제외)이 곧 mapId다 -
    // 파일 안에 mapId를 중복으로 적지 않는다(두 곳에서 관리하면 서로 어긋날 수 있기 때문).
    public class MonsterSpawnPointFile
    {
        public List<MonsterSpawnPointDefinition> Points { get; init; } = new();
    }
}
