using GameServer.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GameServer.Monsters
{
    // MonsterType -> MonsterDefinition(전투 스탯 + AI 튜닝)을 Monsters/MonsterDefinitions.json에서 읽어온다.
    // MonsterSpawnCatalog/DropTableCatalog와 동일한 이유로 서버 부팅 시 한 번에 전부 로드/검증한다 -
    // 데이터 오타를 첫 스폰 순간이 아니라 부팅 시점에 바로 잡기 위함이다. 스폰 포인트가 이 카탈로그에 없는
    // 타입을 참조하는지는 MonsterSpawnCatalog가 로드 시점에 대조하므로, Program.cs는 이 카탈로그를 먼저 로드해야 한다.
    public static class MonsterDefinitionCatalog
    {
        private static readonly ILogger Log = GameLog.For("GameServer.Monsters.MonsterDefinitionCatalog");

        private const string MonstersDirectoryName = "Monsters";
        private const string DefinitionsFileName = "MonsterDefinitions.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static Dictionary<string, MonsterDefinition>? _definitionsByType;

        public static void EnsureLoaded()
        {
            if (_definitionsByType != null)
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, MonstersDirectoryName, DefinitionsFileName);

            // 정의 파일이 없으면 모든 스폰 포인트가 참조 검증에서 실패하므로, 드롭 테이블처럼 조용히 넘기지 않고 바로 막는다.
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"몬스터 정의 파일이 없습니다 : {path}");
            }

            _definitionsByType = ParseAndValidate(File.ReadAllText(path), path);
            Log.LogInformation("몬스터 정의 로드 완료 : {Count}종", _definitionsByType.Count);
        }

        // 파일 읽기와 분리해 둔 파싱/검증 - 테스트가 임의의 JSON으로 검증 규칙을 직접 확인할 수 있다.
        public static Dictionary<string, MonsterDefinition> ParseAndValidate(string json, string sourceName)
        {
            Dictionary<string, MonsterDefinition> result;
            try
            {
                result = JsonSerializer.Deserialize<Dictionary<string, MonsterDefinition>>(json, JsonOptions)
                    ?? new Dictionary<string, MonsterDefinition>();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"몬스터 정의 파일 파싱 실패 : {sourceName}", exception);
            }

            // 0 이하 HP나 음수 속도처럼 서버가 그대로 굴리면 눈치채기 어려운 값은 부팅 시점에 막는다.
            foreach (var (monsterType, definition) in result)
            {
                if (definition.MaxHp <= 0)
                {
                    throw new InvalidOperationException($"몬스터 정의 '{monsterType}'의 MaxHp는 1 이상이어야 합니다 ({definition.MaxHp}).");
                }

                if (definition.AttackPower < 0 || definition.Defense < 0 || definition.ExpReward < 0)
                {
                    throw new InvalidOperationException($"몬스터 정의 '{monsterType}'의 AttackPower/Defense/ExpReward는 0 이상이어야 합니다.");
                }

                if (definition.DetectionRange < 0f || definition.ChaseSpeed <= 0f || definition.LeashRange <= 0f)
                {
                    throw new InvalidOperationException(
                        $"몬스터 정의 '{monsterType}'의 DetectionRange는 0 이상, ChaseSpeed/LeashRange는 0보다 커야 합니다 " +
                        $"(DetectionRange={definition.DetectionRange}, ChaseSpeed={definition.ChaseSpeed}, LeashRange={definition.LeashRange}).");
                }
            }

            return result;
        }

        public static bool Exists(string monsterType)
        {
            EnsureLoaded();
            return _definitionsByType!.ContainsKey(monsterType);
        }

        // 스폰 포인트 로드 시 Exists로 존재를 이미 검증했으므로, 여기서 못 찾는 것은 프로그래밍 오류다.
        public static MonsterDefinition Get(string monsterType)
        {
            EnsureLoaded();
            return _definitionsByType!.TryGetValue(monsterType, out var definition)
                ? definition
                : throw new KeyNotFoundException($"몬스터 정의 '{monsterType}'가 MonsterDefinitions.json에 없습니다.");
        }
    }
}
