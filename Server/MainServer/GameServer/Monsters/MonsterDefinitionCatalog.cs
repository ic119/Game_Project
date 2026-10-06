using GameServer.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        // 보스 스킬 종류(type)를 JSON에서 "AreaSlam"처럼 이름으로 쓰기 위해 enum을 문자열로 읽는다.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        // 보스 스킬이 쓸 수 있는 가장 짧은 예고 시간. 이보다 짧으면 플레이어가 화면을 보고 반응할 수 없어 사실상 회피가 불가능하다.
        public const float MinTelegraphSeconds = 0.3f;

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

                if (definition.AgentRadius <= 0f)
                {
                    throw new InvalidOperationException($"몬스터 정의 '{monsterType}'의 AgentRadius는 0보다 커야 합니다 ({definition.AgentRadius}).");
                }

                if (definition.DetectionRange < 0f || definition.ChaseSpeed <= 0f || definition.LeashRange <= 0f)
                {
                    throw new InvalidOperationException(
                        $"몬스터 정의 '{monsterType}'의 DetectionRange는 0 이상, ChaseSpeed/LeashRange는 0보다 커야 합니다 " +
                        $"(DetectionRange={definition.DetectionRange}, ChaseSpeed={definition.ChaseSpeed}, LeashRange={definition.LeashRange}).");
                }
            }

            // 보스 패턴은 하수인 종류가 다른 정의를 참조하므로 모든 정의를 읽은 뒤에 검증한다.
            foreach (var (monsterType, definition) in result)
            {
                if (definition.BossPattern != null)
                {
                    ValidateBossPattern(monsterType, definition.BossPattern, result);
                }
            }

            return result;
        }

        // 보스 패턴의 수치와 참조를 부팅 시점에 검증한다. 구간(Phases)은 체력 기준 내림차순으로 정렬한다(BossPatternPlanner가 그 순서를 전제한다).
        private static void ValidateBossPattern(string monsterType, BossPatternDefinition pattern, Dictionary<string, MonsterDefinition> all)
        {
            string owner = $"몬스터 정의 '{monsterType}'의 보스 패턴";

            if (pattern.Skills.Count == 0 || pattern.Phases.Count == 0)
            {
                throw new InvalidOperationException($"{owner}에는 스킬(skills)과 체력 구간(phases)이 하나 이상 있어야 합니다.");
            }

            foreach (var (name, skill) in pattern.Skills)
            {
                ValidateBossSkill($"{owner} 스킬 '{name}'", skill, monsterType, all);
            }

            pattern.Phases.Sort((a, b) => b.BelowHpPercent.CompareTo(a.BelowHpPercent));

            if (pattern.Phases[0].BelowHpPercent != 100)
            {
                throw new InvalidOperationException($"{owner}의 첫 체력 구간 기준은 100이어야 합니다(체력이 가득 찬 상태에서 쓸 스킬이 없습니다).");
            }

            for (int i = 0; i < pattern.Phases.Count; i++)
            {
                BossPhaseDefinition phase = pattern.Phases[i];

                if (phase.BelowHpPercent < 1 || phase.BelowHpPercent > 100)
                {
                    throw new InvalidOperationException($"{owner}의 체력 구간 기준(belowHpPercent)은 1~100이어야 합니다 ({phase.BelowHpPercent}).");
                }

                if (i > 0 && pattern.Phases[i - 1].BelowHpPercent == phase.BelowHpPercent)
                {
                    throw new InvalidOperationException($"{owner}에 같은 체력 구간 기준({phase.BelowHpPercent})이 두 번 있습니다.");
                }

                if (phase.SkillIntervalSeconds < 0f)
                {
                    throw new InvalidOperationException($"{owner}의 체력 구간 {phase.BelowHpPercent}% skillIntervalSeconds는 0 이상이어야 합니다.");
                }

                if (phase.Skills.Count == 0)
                {
                    throw new InvalidOperationException($"{owner}의 체력 구간 {phase.BelowHpPercent}%에 스킬이 하나도 없습니다.");
                }

                foreach (string skillName in phase.Skills)
                {
                    if (!pattern.Skills.ContainsKey(skillName))
                    {
                        throw new InvalidOperationException($"{owner}의 체력 구간 {phase.BelowHpPercent}%가 정의되지 않은 스킬 '{skillName}'을 쓰려 합니다.");
                    }
                }
            }
        }

        private static void ValidateBossSkill(string owner, BossSkillDefinition skill, string bossType, Dictionary<string, MonsterDefinition> all)
        {
            if (!Enum.IsDefined(skill.Type))
            {
                throw new InvalidOperationException($"{owner}의 type이 올바르지 않습니다({skill.Type}) - AreaSlam/Charge/Summon 중 하나여야 합니다.");
            }

            if (skill.TelegraphSeconds < MinTelegraphSeconds)
            {
                throw new InvalidOperationException($"{owner}의 telegraphSeconds는 {MinTelegraphSeconds}초 이상이어야 합니다({skill.TelegraphSeconds}) - 더 짧으면 피할 수 없습니다.");
            }

            if (skill.CooldownSeconds < 0f || skill.RecoverSeconds < 0f || skill.DamageMultiplier <= 0f)
            {
                throw new InvalidOperationException($"{owner}의 cooldownSeconds/recoverSeconds는 0 이상, damageMultiplier는 0보다 커야 합니다.");
            }

            switch (skill.Type)
            {
                case BossSkillType.AreaSlam:
                    if (skill.Radius <= 0f || skill.TriggerRange <= 0f)
                    {
                        throw new InvalidOperationException($"{owner}(AreaSlam)의 radius/triggerRange는 0보다 커야 합니다.");
                    }

                    break;

                case BossSkillType.Charge:
                    if (skill.Width <= 0f || skill.Length <= 0f || skill.Speed <= 0f)
                    {
                        throw new InvalidOperationException($"{owner}(Charge)의 width/length/speed는 0보다 커야 합니다.");
                    }

                    if (skill.MinRange < 0f || skill.MaxRange <= skill.MinRange)
                    {
                        throw new InvalidOperationException($"{owner}(Charge)의 minRange는 0 이상, maxRange는 minRange보다 커야 합니다 (minRange={skill.MinRange}, maxRange={skill.MaxRange}).");
                    }

                    break;

                case BossSkillType.Summon:
                    if (skill.SummonCount < 1 || skill.MaxAlive < skill.SummonCount || skill.SummonRadius <= 0f)
                    {
                        throw new InvalidOperationException($"{owner}(Summon)의 summonCount는 1 이상, maxAlive는 summonCount 이상, summonRadius는 0보다 커야 합니다.");
                    }

                    // 오타로 없는 종류를 소환하면 소환 순간 서버가 죽는다. 보스가 보스를 소환하는 것(재귀 소환)도 막는다.
                    if (!all.TryGetValue(skill.SummonMonsterType, out MonsterDefinition? summoned))
                    {
                        throw new InvalidOperationException($"{owner}(Summon)의 summonMonsterType '{skill.SummonMonsterType}'이 몬스터 정의에 없습니다.");
                    }

                    if (summoned.BossPattern != null || skill.SummonMonsterType == bossType)
                    {
                        throw new InvalidOperationException($"{owner}(Summon)은 보스('{skill.SummonMonsterType}')를 소환할 수 없습니다.");
                    }

                    break;
            }
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
