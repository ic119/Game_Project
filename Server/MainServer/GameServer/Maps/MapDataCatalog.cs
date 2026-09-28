using System.Text.Json;

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
