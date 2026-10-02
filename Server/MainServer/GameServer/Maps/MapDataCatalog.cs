using GameServer.Logging;
using Microsoft.Extensions.Logging;
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
        private static readonly ILogger Log = GameLog.For("GameServer.Maps.MapDataCatalog");

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
                Log.LogWarning("맵 데이터 폴더가 없습니다({Directory}) - 입장/부활 위치를 클라이언트 좌표 그대로 사용합니다.", directory);
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

                        // 리스폰을 켠 등급만 시간을 검사한다(0이면 리스폰 없음). 잔존 시간이 1초 미만이면 개봉 브로드캐스트보다
                        // 제거 브로드캐스트가 먼저 나가 클라이언트에 "열림" 기록이 남을 수 있어 막는다.
                        if (spawnCount.RespawnSeconds < 0f
                            || (spawnCount.RespawnSeconds > 0f
                                && (spawnCount.DespawnDelaySeconds < 1f || spawnCount.DespawnDelaySeconds > spawnCount.RespawnSeconds)))
                        {
                            throw new InvalidOperationException($"'{mapId}'의 LootTableKey '{spawnCount.LootTableKey}' 리스폰 시간이 올바르지 않습니다(RespawnSeconds={spawnCount.RespawnSeconds}, DespawnDelaySeconds={spawnCount.DespawnDelaySeconds} - 리스폰을 켜려면 1 <= 잔존 <= 리스폰이어야 합니다).");
                        }
                    }

                    ValidateGate(mapId, data);

                    result[mapId] = data;
                    Log.LogInformation("맵 데이터 로드 완료 : {MapId}", mapId);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"맵 데이터 파일 파싱 실패 : {path}", exception);
                }
            }

            // 도착 맵 데이터가 없는 맵 이동 포탈은 부팅을 막지 않고 경고만 한다 - 도착 맵이 아직 준비되지 않았거나 그 맵의 데이터를
            // 아직 내보내지 않았을 수 있어서(맵을 하나씩 내보내므로 순서에 따라 일시적으로 생긴다). 이 포탈로 맵 이동을 요청하면
            // 서버가 UnknownMap으로 거부한다(ClientSession.HandleMapChangeRequest) - 클라이언트는 사유를 안내하고 이전 맵에 남는다.
            foreach (string problem in FindPortalsWithMissingTargetMap(result))
            {
                Log.LogWarning("도착 맵 데이터(MapData/{{도착 맵}}.json)가 없는 맵 이동 포탈이 있습니다 - 이 포탈로의 이동 요청은 거부됩니다 : {Problem}", problem);
            }

            _mapDataById = result;
        }

        // 던전 게이트 후보(gateCandidates)는 게이트 설정(gatePlan)과 함께 있어야 한다 - 뽑힌 후보가 곧 도착 맵으로 가는 포탈이 되므로
        // 도착 맵/도착 위치가 없으면 그 게이트는 항상 "포탈 근처가 아님"으로 거부된다. 부팅 시점에 막는다.
        private static void ValidateGate(string mapId, MapData data)
        {
            if (data.GateCandidates.Count == 0)
            {
                return;
            }

            if (data.GatePlan is null || string.IsNullOrEmpty(data.GatePlan.TargetMapId) || data.GatePlan.Destination is null)
            {
                throw new InvalidOperationException($"'{mapId}'에 게이트 후보가 있는데 gatePlan(targetMapId/destination)이 올바르지 않습니다.");
            }

            var seenGateIds = new HashSet<string>();
            foreach (MapGateCandidate candidate in data.GateCandidates)
            {
                if (string.IsNullOrEmpty(candidate.Id) || !seenGateIds.Add(candidate.Id))
                {
                    throw new InvalidOperationException($"'{mapId}'의 게이트 후보 id '{candidate.Id}'가 비어 있거나 중복됩니다.");
                }
            }
        }

        // 도착 맵 데이터가 로드된 목록에 없는 MapSwap 포탈(던전 게이트 설정 포함)을 "'출발 맵'의 포탈(x, z) -> '도착 맵'" 형태의 설명으로 돌려준다.
        public static List<string> FindPortalsWithMissingTargetMap(IReadOnlyDictionary<string, MapData> maps)
        {
            var problems = new List<string>();

            foreach (var (mapId, data) in maps)
            {
                foreach (MapPortal portal in data.Portals)
                {
                    if (portal.Type == MapPortal.MapSwapType && !maps.ContainsKey(portal.TargetMapId))
                    {
                        problems.Add($"'{mapId}'의 포탈({portal.X:F1}, {portal.Z:F1}) -> '{portal.TargetMapId}'");
                    }
                }

                if (data.GateCandidates.Count > 0 && data.GatePlan is { } gatePlan && !maps.ContainsKey(gatePlan.TargetMapId))
                {
                    problems.Add($"'{mapId}'의 던전 게이트 후보 {data.GateCandidates.Count}곳 -> '{gatePlan.TargetMapId}'");
                }
            }

            return problems;
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

        // 던전 게이트가 설 수 있는 후보 지점(Unity DungeonGateSpawnPointMarker). 방이 만들어질 때 RoomGateState가 이 중 정확히
        // 한 곳만 뽑는다(없으면 게이트가 없는 맵). 뽑힌 후보는 GatePlan의 도착 정보와 합쳐 MapSwap 포탈로 취급된다.
        public List<MapGateCandidate> GateCandidates { get; init; } = new();

        // 게이트가 가는 곳(Unity DungeonGateSpawnPlan). 후보가 있으면 반드시 있어야 한다.
        public MapGatePlan? GatePlan { get; init; }
    }

    public class MapGateCandidate
    {
        // 클라이언트 후보 마커 GameObject 이름(맵 안에서 고유). 서버가 뽑은 후보를 클라이언트에 알리는 id로 그대로 쓴다.
        public string Id { get; init; } = string.Empty;

        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }

        // 게이트 콜라이더의 수평 반경(m). MapPortal.Radius와 같은 용도.
        public float Radius { get; init; }
    }

    public class MapGatePlan
    {
        // 게이트가 연결되는 도착 맵 id(MapPortal.TargetMapId와 같은 규칙).
        public string TargetMapId { get; init; } = string.Empty;

        // 도착 맵의 진입 지점(MapPortal.Destination과 같은 규칙).
        public MapPoint? Destination { get; init; }
    }

    public class MapChestSpawnCount
    {
        public string LootTableKey { get; init; } = string.Empty;
        public int Count { get; init; }

        // 이 등급의 상자를 연 뒤 새 상자가 다른 후보 지점에 다시 생기기까지의 시간(초). 0이면 리스폰하지 않는다.
        public float RespawnSeconds { get; init; }

        // 연 상자가 열린 채로 남아 있다가 사라지기까지의 시간(초). RespawnSeconds보다 클 수 없다.
        public float DespawnDelaySeconds { get; init; }
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
