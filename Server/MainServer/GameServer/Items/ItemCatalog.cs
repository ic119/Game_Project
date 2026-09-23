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

        private static Dictionary<string, ItemDefinition>? _definitionsByItemId;

        public static void EnsureLoaded()
        {
            if (_definitionsByItemId != null)
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, ItemsDirectoryName, ItemDefinitionsFileName);

            if (!File.Exists(path))
            {
                Console.WriteLine($"[GameServer] 아이템 정의 파일이 없습니다({path}) - 드롭 테이블의 아이템 참조를 검증할 수 없습니다.");
                _definitionsByItemId = new Dictionary<string, ItemDefinition>();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                _definitionsByItemId = JsonSerializer.Deserialize<Dictionary<string, ItemDefinition>>(json, JsonOptions)
                    ?? new Dictionary<string, ItemDefinition>();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"아이템 정의 파일 파싱 실패 : {path}", exception);
            }

            Console.WriteLine($"[GameServer] 아이템 정의 로드 완료 : {_definitionsByItemId.Count}종");
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
    }
}
