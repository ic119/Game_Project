using System.Text.Json;
using GameServer.Items;

namespace GameServer.Monsters
{
    // MonsterType -> 처치 보상(MonsterDropTable)을 Drops/DropTables.json에서 읽어온다.
    // MonsterSpawnCatalog와 동일한 이유로 서버 부팅 시 한 번에 전부 로드/검증한다 - 데이터 오타를
    // 첫 처치 순간이 아니라 부팅 시점에 바로 잡기 위함이다. 드롭 테이블이 없는 몬스터 타입은
    // 조용히 "드롭 없음"으로 취급한다(모든 몬스터가 아이템을 드롭할 필요는 없다).
    public static class DropTableCatalog
    {
        private const string DropsDirectoryName = "Drops";
        private const string DropTablesFileName = "DropTables.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static Dictionary<string, MonsterDropTable>? _tablesByMonsterType;

        public static void EnsureLoaded()
        {
            if (_tablesByMonsterType != null)
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, DropsDirectoryName, DropTablesFileName);

            if (!File.Exists(path))
            {
                Console.WriteLine($"[GameServer] 드롭 테이블 파일이 없습니다({path}) - 모든 몬스터가 드롭 없이 처치됩니다.");
                _tablesByMonsterType = new Dictionary<string, MonsterDropTable>();
                return;
            }

            Dictionary<string, MonsterDropTable> result;
            try
            {
                string json = File.ReadAllText(path);
                result = JsonSerializer.Deserialize<Dictionary<string, MonsterDropTable>>(json, JsonOptions)
                    ?? new Dictionary<string, MonsterDropTable>();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"드롭 테이블 파일 파싱 실패 : {path}", exception);
            }

            // 확률/수량 범위가 잘못된 데이터를 서버가 그대로 굴리기 시작하면(예: DropRate > 1.0으로
            // 사실상 확정 드롭 취급) 눈치채기 어려우므로, 부팅 시점에 바로 막는다.
            foreach (var (monsterType, table) in result)
            {
                if (table.MinGold < 0 || table.MaxGold < table.MinGold)
                {
                    throw new InvalidOperationException($"드롭 테이블 '{monsterType}'의 골드 범위가 잘못되었습니다 (MinGold={table.MinGold}, MaxGold={table.MaxGold}).");
                }

                foreach (var entry in table.Items)
                {
                    if (entry.DropRate < 0f || entry.DropRate > 1f)
                    {
                        throw new InvalidOperationException($"드롭 테이블 '{monsterType}'의 아이템 '{entry.ItemId}' DropRate가 0.0~1.0 범위를 벗어났습니다 ({entry.DropRate}).");
                    }

                    if (entry.MinQty <= 0 || entry.MaxQty < entry.MinQty)
                    {
                        throw new InvalidOperationException($"드롭 테이블 '{monsterType}'의 아이템 '{entry.ItemId}' 수량 범위가 잘못되었습니다 (MinQty={entry.MinQty}, MaxQty={entry.MaxQty}).");
                    }

                    // 오타로 존재하지 않는 ItemId를 참조하면 클라이언트가 처치 순간 알 수 없는 아이템을
                    // 받게 되므로, 부팅 시점에 ItemCatalog와 대조해 바로 막는다.
                    if (!ItemCatalog.Exists(entry.ItemId))
                    {
                        throw new InvalidOperationException($"드롭 테이블 '{monsterType}'의 아이템 '{entry.ItemId}'이 ItemDefinitions.json에 존재하지 않습니다.");
                    }
                }
            }

            _tablesByMonsterType = result;
            Console.WriteLine($"[GameServer] 드롭 테이블 로드 완료 : {result.Count}종");
        }

        // 골드는 항상 계산되고(범위가 0~0이면 0), 아이템은 항목마다 독립적으로 DropRate를 굴려 0개 이상이
        // 동시에 나올 수 있다. 해당 MonsterType에 정의된 드롭 테이블이 없으면 골드 0 + 빈 아이템 목록을 반환한다.
        public static (int Gold, List<(string ItemId, int Qty)> Items) Roll(string monsterType)
        {
            EnsureLoaded();

            if (!_tablesByMonsterType!.TryGetValue(monsterType, out var table))
            {
                return (0, new List<(string, int)>());
            }

            int gold = table.MaxGold > table.MinGold
                ? Random.Shared.Next(table.MinGold, table.MaxGold + 1)
                : table.MinGold;

            var droppedItems = new List<(string ItemId, int Qty)>();
            foreach (var entry in table.Items)
            {
                if (Random.Shared.NextSingle() > entry.DropRate)
                {
                    continue;
                }

                int qty = entry.MaxQty > entry.MinQty
                    ? Random.Shared.Next(entry.MinQty, entry.MaxQty + 1)
                    : entry.MinQty;

                droppedItems.Add((entry.ItemId, qty));
            }

            return (gold, droppedItems);
        }
    }
}
