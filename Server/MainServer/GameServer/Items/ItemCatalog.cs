using System.Text.Json;

namespace GameServer.Items
{
    // Items/ItemDefinitions.json(ItemId -> ItemDefinition)을 부팅 시 한 번 로드한다.
    // MonsterSpawnCatalog/DropTableCatalog와 동일한 이유(오타를 부팅 시점에 바로 잡기)로,
    // DropTableCatalog가 이 카탈로그를 이용해 존재하지 않는 ItemId를 참조하는 드롭 항목을 걸러낸다.
    public static class ItemCatalog
    {
        private const string ItemsDirectoryName = "Items";
        private const string ItemDefinitionsFileName = "ItemDefinitions.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly object InitLock = new();

        private static Dictionary<string, ItemDefinition>? _definitionsByItemId;

        // Grade -> 그 등급인 itemId 목록. DropTableEntry.Grade("등급 무작위 드롭") 판정용으로 EnsureLoaded에서
        // 한 번만 구성해둔다 - Roll()마다 전체 카탈로그를 훑지 않기 위함이다.
        private static Dictionary<string, List<string>>? _itemIdsByGrade;

        // 원래는 GameServer.Program이 부팅 시 한 번만 호출해 단일 스레드에서 끝나는 걸 전제했는데(다른
        // *Catalog들과 동일), 테스트(xUnit)는 서로 다른 테스트 클래스를 병렬로 돌려 이 초기화가 동시에
        // 여러 스레드에서 호출될 수 있다는 게 드러났다 - 락 없이 두면 Dictionary가 깨진다.
        public static void EnsureLoaded()
        {
            if (_definitionsByItemId != null)
            {
                return;
            }

            lock (InitLock)
            {
                if (_definitionsByItemId != null)
                {
                    return;
                }

                string path = Path.Combine(AppContext.BaseDirectory, ItemsDirectoryName, ItemDefinitionsFileName);

                if (!File.Exists(path))
                {
                    Console.WriteLine($"[GameServer] 아이템 정의 파일이 없습니다({path}) - 드롭 테이블의 아이템 참조를 검증할 수 없습니다.");
                    _itemIdsByGrade = new Dictionary<string, List<string>>();
                    _definitionsByItemId = new Dictionary<string, ItemDefinition>();
                    return;
                }

                Dictionary<string, ItemDefinition> definitions;
                try
                {
                    string json = File.ReadAllText(path);
                    definitions = JsonSerializer.Deserialize<Dictionary<string, ItemDefinition>>(json, JsonOptions)
                        ?? new Dictionary<string, ItemDefinition>();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"아이템 정의 파일 파싱 실패 : {path}", exception);
                }

                var itemIdsByGrade = new Dictionary<string, List<string>>();
                foreach (var (itemId, definition) in definitions)
                {
                    if (string.IsNullOrEmpty(definition.Grade))
                    {
                        continue;
                    }

                    if (!itemIdsByGrade.TryGetValue(definition.Grade, out List<string>? list))
                    {
                        list = new List<string>();
                        itemIdsByGrade[definition.Grade] = list;
                    }

                    list.Add(itemId);
                }

                _itemIdsByGrade = itemIdsByGrade;
                // 다른 스레드가 락 밖의 null 체크(EnsureLoaded 진입부, Exists 등)에서 절반만 채워진 상태를 보지
                // 않도록, 완전히 다 만든 뒤 마지막에 대입한다.
                _definitionsByItemId = definitions;

                Console.WriteLine($"[GameServer] 아이템 정의 로드 완료 : {definitions.Count}종");
            }
        }

        public static bool Exists(string itemId)
        {
            EnsureLoaded();
            return _definitionsByItemId!.ContainsKey(itemId);
        }

        // Combat.CombatStatCalculator가 장착 중인 아이템의 공격력/방어력 보너스를 조회할 때 쓴다.
        public static bool TryGet(string itemId, out ItemDefinition definition)
        {
            EnsureLoaded();
            return _definitionsByItemId!.TryGetValue(itemId, out definition!);
        }

        // grade에 해당하는 아이템이 하나라도 있는지. DropTableCatalog가 부팅 시 Grade 드롭 항목을 검증할 때 쓴다.
        public static bool HasAnyOfGrade(string grade)
        {
            EnsureLoaded();
            return _itemIdsByGrade!.TryGetValue(grade, out List<string>? list) && list.Count > 0;
        }

        // grade 풀에서 itemId 하나를 무작위로 고른다. DropTableCatalog.Roll이 Grade 드롭 항목을 실제로 굴릴 때 쓴다.
        public static bool TryGetRandomByGrade(string grade, out string itemId)
        {
            EnsureLoaded();

            if (!_itemIdsByGrade!.TryGetValue(grade, out List<string>? list) || list.Count == 0)
            {
                itemId = string.Empty;
                return false;
            }

            itemId = list[Random.Shared.Next(list.Count)];
            return true;
        }
    }
}
