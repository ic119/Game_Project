using GameServer.Logging;
using GameServer.Navigation;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GameServer.Monsters
{
    // mapId(GameRoom 라우팅 키, 클라이언트 AddressableAssetKey 문자열과 동일)별 스폰 포인트 목록을
    // SpawnPoints/{mapId}.json에서 읽어온다. Unity에서 마커로 배치한 뒤 내보낸 파일을 이 폴더에 두면
    // 되고, GameRoom/ClientSession 등 호출측은 GetPointsForMap만 알면 되므로 영향받지 않는다.
    public static class MonsterSpawnCatalog
    {
        private static readonly ILogger Log = GameLog.For("GameServer.Monsters.MonsterSpawnCatalog");

        private const string SpawnPointsDirectoryName = "SpawnPoints";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Program.cs가 서버 시작 시 한 번 EnsureLoaded()를 호출해 이 시점에 전부 파싱해둔다 - 그래야
        // 스폰 데이터가 잘못돼도 첫 플레이어가 접속하는 순간이 아니라 서버 부팅 시점에 바로 알 수 있다.
        private static Dictionary<string, List<MonsterSpawnPointDefinition>>? _pointsByMap;

        public static void EnsureLoaded()
        {
            if (_pointsByMap != null)
            {
                return;
            }

            string directory = Path.Combine(AppContext.BaseDirectory, SpawnPointsDirectoryName);
            var result = new Dictionary<string, List<MonsterSpawnPointDefinition>>();

            if (!Directory.Exists(directory))
            {
                Log.LogWarning("스폰 포인트 폴더가 없습니다({Directory}) - 모든 맵이 몬스터 없이 시작합니다.", directory);
                _pointsByMap = result;
                return;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
            {
                string mapId = Path.GetFileNameWithoutExtension(path);

                // 오타/형식 오류로 몬스터가 하나도 없는 맵을 조용히 띄우는 대신, 여기서 서버 시작 자체를
                // 막는다 - 나중에 눈치채는 것보다 부팅 시점에 바로 아는 편이 낫다.
                try
                {
                    string json = File.ReadAllText(path);
                    MonsterSpawnPointFile? file = JsonSerializer.Deserialize<MonsterSpawnPointFile>(json, JsonOptions);
                    List<MonsterSpawnPointDefinition> points = file?.Points ?? new List<MonsterSpawnPointDefinition>();

                    // Entries가 비어있으면 SpawnMonsterAtPoint가 무작위 선택 시 바로 예외를 던진다 -
                    // 첫 플레이어가 그 맵에 들어가는 순간 서버가 죽는 것보다, 부팅 시점에 바로 막는 편이 낫다.
                    foreach (MonsterSpawnPointDefinition point in points)
                    {
                        if (point.Entries.Count == 0)
                        {
                            throw new InvalidOperationException($"스폰 포인트 '{point.PointId}'에 몬스터 타입(Entries)이 하나도 없습니다.");
                        }

                        if (point.SpawnJitterRadius < 0f)
                        {
                            throw new InvalidOperationException($"스폰 포인트 '{point.PointId}'의 SpawnJitterRadius는 0 이상이어야 합니다 ({point.SpawnJitterRadius}).");
                        }

                        // 영역 밖에 있는 포인트는 스폰하자마자 영역 밖에 갇힌 몬스터를 만든다 - 좌표/영역 오타를 부팅 시점에 막는다.
                        if (!point.AllowsPosition(point.X, point.Z))
                        {
                            throw new InvalidOperationException($"스폰 포인트 '{point.PointId}'의 좌표가 자신의 활동 영역(Area) 밖에 있습니다.");
                        }

                        // 이동 격자가 있는 맵에서 포인트가 가구/벽 안(이동 불가 칸)에 있으면 몬스터가 그 안에 갇혀 스폰된다.
                        NavGrid? navGrid = NavGridCatalog.GetRaw(mapId);
                        if (navGrid != null && !navGrid.IsWalkableAt(point.X, point.Z))
                        {
                            throw new InvalidOperationException($"스폰 포인트 '{point.PointId}'의 좌표({point.X}, {point.Z})가 이동 격자의 이동 불가 칸(가구/벽 안)에 있습니다.");
                        }

                        foreach (MonsterSpawnEntry entry in point.Entries)
                        {
                            if (entry.Weight < 1)
                            {
                                throw new InvalidOperationException($"스폰 포인트 '{point.PointId}'의 '{entry.MonsterType}' Weight는 1 이상이어야 합니다 ({entry.Weight}).");
                            }

                            // 오타로 정의에 없는 타입을 참조하면 스폰 순간에 서버가 죽으므로, 부팅 시점에 바로 막는다.
                            if (!MonsterDefinitionCatalog.Exists(entry.MonsterType))
                            {
                                throw new InvalidOperationException($"스폰 포인트 '{point.PointId}'의 몬스터 타입 '{entry.MonsterType}'이 MonsterDefinitions.json에 존재하지 않습니다.");
                            }
                        }
                    }

                    result[mapId] = points;
                    Log.LogInformation("스폰 포인트 로드 완료 : {MapId} ({Count}개)", mapId, points.Count);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"스폰 포인트 파일 파싱 실패 : {path}", exception);
                }
            }

            _pointsByMap = result;
        }

        public static List<MonsterSpawnPointDefinition> GetPointsForMap(string mapId)
        {
            EnsureLoaded();
            return _pointsByMap!.TryGetValue(mapId, out var points) ? points : new List<MonsterSpawnPointDefinition>();
        }
    }
}
