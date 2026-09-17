using System.Collections.Generic;

namespace GameServer.Monsters
{
    // mapId(GameRoom 라우팅 키, 클라이언트 AddressableAssetKey 문자열과 동일)별 스폰 포인트 목록.
    // 지금은 맵마다 손으로 등록하지만, 맵이 늘어나면 이 클래스만 설정 파일 로더로 바꾸면 된다 -
    // GameRoom/ClientSession 등 호출측은 GetPointsForMap만 알면 되므로 영향받지 않는다.
    public static class MonsterSpawnCatalog
    {
        private static readonly Dictionary<string, List<MonsterSpawnPointDefinition>> PointsByMap = new()
        {
            ["Farm"] = new List<MonsterSpawnPointDefinition>
            {
                // 두 포인트가 서로 다른 개체수/리스폰 시간을 가진다는 걸 보여주기 위해 일부러 다르게 설정했다 -
                // 한 포인트가 전멸해도 다른 포인트의 생존/타이머에는 영향을 주지 않는다(GameRoom 참고).
                new()
                {
                    PointId = "Farm_Boar_North",
                    MonsterType = "RedBoar",
                    X = -12.85f,
                    Y = 0.12f,
                    Z = 4.15f,
                    RotationY = 180f,
                    MaxAlive = 2,
                    RespawnSeconds = 20f,
                    MaxHp = 30,
                    AttackPower = 5,
                    Defense = 0
                },
                new()
                {
                    PointId = "Farm_Boar_West",
                    MonsterType = "RedBoar",
                    X = -20.85f,
                    Y = 0.07f,
                    Z = -1.85f,
                    RotationY = 90f,
                    MaxAlive = 1,
                    RespawnSeconds = 35f,
                    MaxHp = 45,
                    AttackPower = 7,
                    Defense = 2
                }
            }
        };

        public static List<MonsterSpawnPointDefinition> GetPointsForMap(string mapId)
        {
            return PointsByMap.TryGetValue(mapId, out var points) ? points : new List<MonsterSpawnPointDefinition>();
        }
    }
}
