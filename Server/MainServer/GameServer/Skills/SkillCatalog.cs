using GameServer.Logging;
using Microsoft.Extensions.Logging;
using Shared;
using System.Text.Json;

namespace GameServer.Skills
{
    // Skills/SkillDefinitions.json(배열)을 부팅 시 한 번 로드하고 검증한다. 오타/범위를 벗어난 값/해금 레벨 불일치는
    // 서버 시작 시 바로 거부한다(다른 *Catalog와 같은 방식). 키는 (무기 종류, 슬롯)이다.
    public static class SkillCatalog
    {
        private static readonly ILogger Log = GameLog.For("GameServer.Skills.SkillCatalog");

        private const string SkillsDirectoryName = "Skills";
        private const string SkillDefinitionsFileName = "SkillDefinitions.json";

        public const int SlotCount = 4;

        // 슬롯 -> 해금 레벨. 기획(액티브스킬_기획안.txt)에서 확정된 값이며 클라이언트 UI도 같은 값을 쓴다.
        public static readonly IReadOnlyDictionary<int, int> UnlockLevelBySlot = new Dictionary<int, int>
        {
            [1] = 3,
            [2] = 6,
            [3] = 9,
            [4] = 12
        };

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly object InitLock = new();

        private static Dictionary<(WeaponKind Weapon, int Slot), SkillDefinition>? _skills;

        // 테스트(xUnit)가 병렬로 돌므로 ItemCatalog와 같이 락으로 한 번만 로드한다.
        public static void EnsureLoaded()
        {
            if (_skills != null)
            {
                return;
            }

            lock (InitLock)
            {
                if (_skills != null)
                {
                    return;
                }

                string path = Path.Combine(AppContext.BaseDirectory, SkillsDirectoryName, SkillDefinitionsFileName);
                if (!File.Exists(path))
                {
                    Log.LogWarning("스킬 정의 파일이 없습니다({Path}) - 액티브 스킬을 쓸 수 없습니다.", path);
                    _skills = new Dictionary<(WeaponKind Weapon, int Slot), SkillDefinition>();
                    return;
                }

                List<SkillDefinition> definitions;
                try
                {
                    definitions = JsonSerializer.Deserialize<List<SkillDefinition>>(File.ReadAllText(path), JsonOptions) ?? new List<SkillDefinition>();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"스킬 정의 파일 파싱 실패 : {path}", exception);
                }

                var skills = Build(definitions);
                _skills = skills;
                Log.LogInformation("스킬 정의 로드 완료 : {Count}종", skills.Count);
            }
        }

        // 검증 로직을 테스트에서 따로 부를 수 있게 공개한다. 잘못된 항목이 있으면 InvalidOperationException.
        public static Dictionary<(WeaponKind Weapon, int Slot), SkillDefinition> Build(IEnumerable<SkillDefinition> definitions)
        {
            var result = new Dictionary<(WeaponKind Weapon, int Slot), SkillDefinition>();
            var ids = new HashSet<string>();

            foreach (SkillDefinition skill in definitions)
            {
                string label = string.IsNullOrEmpty(skill.Id) ? "(id 없음)" : skill.Id;

                void Require(bool condition, string message)
                {
                    if (!condition)
                    {
                        throw new InvalidOperationException($"스킬 정의 오류 '{label}': {message}");
                    }
                }

                Require(!string.IsNullOrEmpty(skill.Id), "Id가 비어 있습니다.");
                Require(ids.Add(skill.Id), "Id가 중복됩니다.");

                bool weaponOk = Enum.TryParse(skill.WeaponType, ignoreCase: true, out WeaponKind weapon) && weapon != WeaponKind.None && Enum.IsDefined(weapon);
                Require(weaponOk, $"WeaponType '{skill.WeaponType}'을(를) 알 수 없습니다.");

                bool slotOk = UnlockLevelBySlot.TryGetValue(skill.Slot, out int unlockLevel);
                Require(slotOk, $"Slot은 1~{SlotCount}여야 합니다.");
                Require(skill.UnlockLevel == unlockLevel, $"UnlockLevel은 슬롯 {skill.Slot}의 해금 레벨 {unlockLevel}이어야 합니다.");
                Require(skill.ManaCost > 0, "ManaCost는 0보다 커야 합니다.");
                Require(skill.CooldownSeconds > 0f, "CooldownSeconds는 0보다 커야 합니다.");
                Require(skill.CastLockSeconds >= 0f, "CastLockSeconds는 0 이상이어야 합니다.");
                Require(skill.DamageMultiplier > 0f, "DamageMultiplier는 0보다 커야 합니다.");
                Require(skill.Hits is >= 1 and <= 10, "Hits는 1~10이어야 합니다.");
                Require(skill.HitDelaySeconds >= 0f && skill.HitIntervalSeconds >= 0f, "HitDelaySeconds/HitIntervalSeconds는 0 이상이어야 합니다.");
                Require(skill.Hits == 1 || skill.HitIntervalSeconds > 0f, "연타(Hits>1)는 HitIntervalSeconds가 필요합니다.");
                Require(skill.MaxTargets >= 1, "MaxTargets는 1 이상이어야 합니다.");

                bool shapeOk = Enum.TryParse(skill.Shape, ignoreCase: true, out SkillShape shape) && Enum.IsDefined(shape);
                Require(shapeOk, $"Shape '{skill.Shape}'을(를) 알 수 없습니다.");

                switch (shape)
                {
                    case SkillShape.Line:
                        Require(skill.Length > 0f && skill.Width > 0f, "Line은 Length와 Width가 0보다 커야 합니다.");
                        break;
                    case SkillShape.Circle:
                        Require(skill.Radius > 0f && skill.ForwardOffset >= 0f, "Circle은 Radius가 0보다 크고 ForwardOffset이 0 이상이어야 합니다.");
                        break;
                    case SkillShape.Cone:
                        Require(skill.Length > 0f && skill.Angle is > 0f and <= 360f, "Cone은 Length가 0보다 크고 Angle이 0~360도여야 합니다.");
                        break;
                }

                Require(result.TryAdd((weapon, skill.Slot), skill), $"{weapon} 슬롯 {skill.Slot}이 중복됩니다.");
            }

            return result;
        }

        public static bool TryGet(WeaponKind weapon, int slot, out SkillDefinition skill)
        {
            EnsureLoaded();
            return _skills!.TryGetValue((weapon, slot), out skill!);
        }

        public static IEnumerable<SkillDefinition> All()
        {
            EnsureLoaded();
            return _skills!.Values;
        }
    }
}
