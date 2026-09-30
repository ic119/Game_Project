using System.Text.Json;
using GameServer.Monsters;

namespace GameServer.Maps
{
    // mapId(GameRoom 라우팅 키)별 맵 좌표 데이터를 MapData/{mapId}.json에서 읽어온다. Unity의
    // Tools/Map/Export Map Data From Selected Prefab(MapDataExporter)이 이 폴더로 직접 내보낸다.
    // GameServer는 이 값으로 입장/부활 위치를 직접 정한다 - 클라이언트가 보낸 좌표로 순간이동을 허용하면
    // 이동 속도 검증을 우회할 수 있기 때문이다.
    public static class MapDataCatalog
    {
        private const string MapDataDirectoryName = "MapData";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static Dictionary<string, MapData>? _mapDataById;

        // Program.cs가 서버 시작 시 한 번 호출한다 - 파일이 잘못돼 있으면 부팅 시점에 바로 알 수 있게 한다.
        public static void EnsureLoaded()
        {
            if (_mapDataById != null)
            {
                return;
            }

            string directory = Path.Combine(AppContext.BaseDirectory, MapDataDirectoryName);
            var result = new Dictionary<string, MapData>();

            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"[GameServer] 맵 데이터 폴더가 없습니다({directory}) - 입장/부활 위치를 클라이언트 좌표 그대로 사용합니다.");
                _mapDataById = result;
                return;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
            {
                string mapId = Path.GetFileNameWithoutExtension(path);
                try
                {
                    string json = File.ReadAllText(path);
                    MapData data = JsonSerializer.Deserialize<MapData>(json, JsonOptions)
                        ?? throw new InvalidOperationException("빈 파일입니다.");

                    if (data.RespawnPoint is null)
                    {
                        throw new InvalidOperationException("respawnPoint가 없습니다.");
                    }

                    foreach (MapPortal portal in data.Portals)
                    {
                        if (portal.Destination is null
                            || (portal.Type != MapPortal.MapSwapType && portal.Type != MapPortal.CoordinateTeleportType)
                            || (portal.Type == MapPortal.MapSwapType && string.IsNullOrEmpty(portal.TargetMapId)))
                        {
                            throw new InvalidOperationException($"포탈 데이터가 올바르지 않습니다(type={portal.Type}, targetMapId={portal.TargetMapId}).");
                        }
                    }

                    var seenChestIds = new HashSet<string>();
                    foreach (MapChest chest in data.Chests)
                    {
                        if (string.IsNullOrEmpty(chest.Id))
                        {
                            throw new InvalidOperationException($"'{mapId}'에 id가 없는 상자가 있습니다.");
                        }

                        if (!seenChestIds.Add(chest.Id))
                        {
                            throw new InvalidOperationException($"'{mapId}'에 중복된 상자 id가 있습니다 : {chest.Id}");
                        }

                        // ItemCatalog -> DropTableCatalog -> MapDataCatalog 순서로 로드해야(Program.cs) 이 시점에
                        // DropTableCatalog가 이미 준비돼 있다. 오타로 존재하지 않는 LootTableKey를 참조하면
                        // 그 상자는 항상 빈 손으로 열리는데, 첫 플레이어가 열어보고 나서야 눈치채는 대신 여기서 막는다.
                        if (string.IsNullOrEmpty(chest.LootTableKey) || !DropTableCatalog.HasTable(chest.LootTableKey))
                        {
                            throw new InvalidOperationException($"'{mapId}'의 상자 '{chest.Id}'가 참조하는 LootTableKey '{chest.LootTableKey}'가 Drops/DropTables.json에 없습니다.");
                        }
                    }

                    // 후보 상자(chestCandidates)는 고정 상자와 같은 id 공간을 쓴다 - 뽑힌 후보가 곧 chestId가 되기 때문이다.
                    var candidateCountByKey = new Dictionary<string, int>();
                    foreach (MapChest candidate in data.ChestCandidates)
                    {
                        if (string.IsNullOrEmpty(candidate.Id) || !seenChestIds.Add(candidate.Id))
                        {
                            throw new InvalidOperationException($"'{mapId}'의 상자 후보 id '{candidate.Id}'가 비어 있거나 다른 상자/후보와 중복됩니다.");
                        }

                        if (string.IsNullOrEmpty(candidate.LootTableKey) || !DropTableCatalog.HasTable(candidate.LootTableKey))
                        {
                            throw new InvalidOperationException($"'{mapId}'의 상자 후보 '{candidate.Id}'가 참조하는 LootTableKey '{candidate.LootTableKey}'가 Drops/DropTables.json에 없습니다.");
                        }

                        candidateCountByKey[candidate.LootTableKey] = candidateCountByKey.GetValueOrDefault(candidate.LootTableKey) + 1;
                    }

                    // 뽑을 개수가 후보 수보다 많으면 매번 "모자란 채로" 방이 만들어진다 - 부팅 시점에 막는다.
                    var seenCountKeys = new HashSet<string>();
                    foreach (MapChestSpawnCount spawnCount in data.ChestSpawnCounts)
                    {
                        if (string.IsNullOrEmpty(spawnCount.LootTableKey) || !seenCountKeys.Add(spawnCount.LootTableKey))
                        {
                            throw new InvalidOperationException($"'{mapId}'의 chestSpawnCounts lootTableKey '{spawnCount.LootTableKey}'가 비어 있거나 중복됩니다.");
                        }

                        int available = candidateCountByKey.GetValueOrDefault(spawnCount.LootTableKey);
                        if (spawnCount.Count < 0 || spawnCount.Count > available)
                        {
                            throw new InvalidOperationException($"'{mapId}'의 LootTableKey '{spawnCount.LootTableKey}' 뽑을 개수({spawnCount.Count})가 0~후보 수({available}) 범위를 벗어났습니다.");
                        }
                    }

                    result[mapId] = data;
                    Console.WriteLine($"[GameServer] 맵 데이터 로드 완료 : {mapId}");
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"맵 데이터 파일 파싱 실패 : {path}", exception);
                }
            }

            _mapDataById = result;
        }

        public static bool TryGet(string mapId, out MapData data)
        {
            EnsureLoaded();
            return _mapDataById!.TryGetValue(mapId, out data!);
        }
    }

    public class MapData
    {
        // 입장/부활 위치(클라이언트 GameSceneManager가 찾는 "RespawnPoint" 오브젝트와 같은 좌표).
        public MapPoint? RespawnPoint { get; init; }

        // 이 맵에 배치된 포탈. 맵 이동(Game_MapChangeRequest)과 같은 맵 안 좌표 이동 포탈의 순간이동을 검증하는 기준이다.
        public List<MapPortal> Portals { get; init; } = new();

        // 이 맵에 배치된 보물상자. GameRoom.TryOpenChest가 사거리/선착순 판정 기준으로 쓴다.
        public List<MapChest> Chests { get; init; } = new();

        // 상자가 설 수 있는 후보 지점(Unity TreasureChestSpawnPointMarker). 형식이 MapChest와 같다 - 방이 만들어질 때
        // ChestSpawnSelector가 ChestSpawnCounts만큼 뽑아 Chests와 똑같이 취급한다(GameRoom.ActiveChests).
        public List<MapChest> ChestCandidates { get; init; } = new();

        // 등급(LootTableKey)별로 후보 중 몇 개를 뽑을지(Unity TreasureChestSpawnPlan).
        public List<MapChestSpawnCount> ChestSpawnCounts { get; init; } = new();
    }

    public class MapChestSpawnCount
    {
        public string LootTableKey { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    public class MapChest
    {
        // 클라이언트 TreasureChestInteractionController.chestId와 정확히 일치해야 한다(맵 안에서 고유).
        public string Id { get; init; } = string.Empty;

        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }

        // 상자 콜라이더의 수평 반경(m). MapPortal.Radius와 같은 용도.
        public float Radius { get; init; }

        // Drops/DropTables.json의 키(몬스터 타입과 같은 딕셔너리를 공유한다 - DropTableCatalog.Roll 참고).
        public string LootTableKey { get; init; } = string.Empty;

        // MapPortal.IsWithinRange와 같은 이유로 여유 거리를 둔다(이동은 0.1초마다만 보고되므로).
        private const float EntryTolerance = 1.5f;

        public bool IsWithinRange(float x, float z)
        {
            float dx = x - X;
            float dz = z - Z;
            float range = Radius + EntryTolerance;
            return dx * dx + dz * dz <= range * range;
        }
    }

    public class MapPortal
    {
        public const string MapSwapType = "MapSwap";
        public const string CoordinateTeleportType = "CoordinateTeleport";

        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }

        // 포탈 콜라이더의 수평 반경(m).
        public float Radius { get; init; }

        public string Type { get; init; } = string.Empty;

        // MapSwap일 때 도착 맵 id. CoordinateTeleport면 비어 있다.
        public string TargetMapId { get; init; } = string.Empty;

        // 도착 위치(MapSwap이면 도착 맵의 진입 지점, CoordinateTeleport면 같은 맵 안의 목적지).
        public MapPoint? Destination { get; init; }

        // 포탈 반경에 이 여유를 더한 거리 안에 있으면 포탈을 탄 것으로 인정한다 - 클라이언트는 이동을 0.1초마다만
        // 보내고 포탈은 진입 후 지연(teleportDelay) 뒤에 동작하므로, 서버가 마지막으로 아는 위치가 반경을 조금 벗어날 수 있다.
        private const float EntryTolerance = 2f;

        public bool IsWithinRange(float x, float z)
        {
            float dx = x - X;
            float dz = z - Z;
            float range = Radius + EntryTolerance;
            return dx * dx + dz * dz <= range * range;
        }
    }

    public class MapPoint
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float RotationY { get; init; }
    }
}
