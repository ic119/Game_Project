using System.Collections.Concurrent;
using System.Text.Json;
using GameServer.Logging;
using Microsoft.Extensions.Logging;

namespace GameServer.Navigation
{
    // mapId별 이동 가능 격자를 NavGrids/{mapId}.json에서 읽어온다. Unity의 Tools/Monster/Export Spawn Points가
    // 활동 영역(방)이 있는 마커를 발견하면 이 폴더로 함께 내보낸다. 파일이 없는 맵(트인 필드)은 격자가 없고,
    // 그런 맵의 몬스터는 기존처럼 직선으로 움직인다. MonsterSpawnCatalog/DropTableCatalog와 같은 이유로 서버 부팅 시
    // 한 번에 전부 로드/검증한다.
    public static class NavGridCatalog
    {
        private static readonly ILogger Log = GameLog.For("GameServer.Navigation.NavGridCatalog");

        private const string NavGridsDirectoryName = "NavGrids";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static Dictionary<string, NavGrid>? _rawGridsByMap;

        // (mapId, 반경(cm)) -> 몬스터 몸통 반경만큼 부풀린 격자. 몬스터마다 다시 계산하지 않도록 한 번만 만들어 재사용한다.
        private static readonly ConcurrentDictionary<(string MapId, int RadiusCm), NavGrid> DilatedGrids = new();

        // JSON 파일 형식(Unity MonsterNavGridExporter와 같아야 한다).
        private sealed class NavGridFile
        {
            public float CellSize { get; init; }
            public float OriginX { get; init; }
            public float OriginZ { get; init; }
            public List<string> Rows { get; init; } = new();
        }

        public static void EnsureLoaded()
        {
            if (_rawGridsByMap != null)
            {
                return;
            }

            var result = new Dictionary<string, NavGrid>();
            string directory = Path.Combine(AppContext.BaseDirectory, NavGridsDirectoryName);

            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
                {
                    string mapId = Path.GetFileNameWithoutExtension(path);
                    NavGrid grid = ParseAndValidate(File.ReadAllText(path), path);
                    result[mapId] = grid;
                    Log.LogInformation("이동 격자 로드 완료 : {MapId} ({Width}x{Height}칸, 칸 크기 {CellSize}m)", mapId, grid.Width, grid.Height, grid.CellSize);
                }
            }

            _rawGridsByMap = result;
        }

        // 파일 읽기와 분리해 둔 파싱/검증 - 테스트가 임의의 JSON으로 검증 규칙을 직접 확인할 수 있다.
        public static NavGrid ParseAndValidate(string json, string sourceName)
        {
            try
            {
                NavGridFile file = JsonSerializer.Deserialize<NavGridFile>(json, JsonOptions)
                    ?? throw new InvalidOperationException("빈 파일입니다.");

                if (file.CellSize <= 0f)
                {
                    throw new InvalidOperationException($"cellSize는 0보다 커야 합니다 ({file.CellSize}).");
                }

                return NavGrid.FromRows(file.CellSize, file.OriginX, file.OriginZ, file.Rows);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"이동 격자 파일 파싱 실패 : {sourceName}", exception);
            }
        }

        // 부풀리지 않은 원본 격자. 스폰 포인트 좌표 검증처럼 "이 자리가 장애물 안인가"를 볼 때 쓴다.
        public static NavGrid? GetRaw(string mapId)
        {
            EnsureLoaded();
            return _rawGridsByMap!.TryGetValue(mapId, out NavGrid? grid) ? grid : null;
        }

        // 몸통 반경(agentRadius, m)만큼 장애물을 부풀린 격자. 이 맵에 격자가 없으면 null(직선 이동으로 대체).
        public static NavGrid? Get(string mapId, float agentRadius)
        {
            NavGrid? raw = GetRaw(mapId);
            if (raw == null)
            {
                return null;
            }

            int radiusCm = (int)MathF.Round(agentRadius * 100f);
            return DilatedGrids.GetOrAdd((mapId, radiusCm), _ => raw.Dilate(radiusCm / 100f));
        }
    }
}
