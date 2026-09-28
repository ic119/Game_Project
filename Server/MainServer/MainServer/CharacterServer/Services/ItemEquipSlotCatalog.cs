using System.Text.Json;

namespace MainServer.CharacterServer.Services
{
    // 아이템별 장착 가능 슬롯. GameServer의 Items/ItemDefinitions.json을 그대로 읽는다(빌드 시 이 서버 출력 폴더로 복사된다 -
    // MainServer.csproj/Dockerfile 참고). 예전에는 보유한 아이템이면 종류와 상관없이 아무 슬롯에나 장착돼, 물약이나 재료를
    // 무기 칸에 넣을 수 있었고 같은 장비를 엉뚱한 칸에 넣는 것도 막지 못했다. 이제 정의에 equipSlot이 있는 아이템만,
    // 그 슬롯에만 장착할 수 있다. 새 장비를 추가할 때 ItemDefinitions.json에 equipSlot을 적어야 한다(클라이언트 ItemData.equipSlotType과 같은 이름).
    public static class ItemEquipSlotCatalog
    {
        private static readonly string DefinitionsPath = Path.Combine(AppContext.BaseDirectory, "Items", "ItemDefinitions.json");

        private static Dictionary<string, string?>? _equipSlotByItemId;

        // 서버 시작 시 호출한다. 파일이 없거나 잘못됐으면 여기서 바로 실패시킨다(첫 장착 요청 때가 아니라).
        public static void EnsureLoaded(IReadOnlyCollection<string> validEquipSlots)
        {
            if (!File.Exists(DefinitionsPath))
                throw new InvalidOperationException($"아이템 정의 파일이 없습니다: {DefinitionsPath}");

            var definitions = JsonSerializer.Deserialize<Dictionary<string, ItemDefinitionEntry>>(
                File.ReadAllText(DefinitionsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Dictionary<string, ItemDefinitionEntry>();

            foreach (var (itemId, definition) in definitions)
            {
                if (definition.EquipSlot is { } slot && !validEquipSlots.Contains(slot))
                    throw new InvalidOperationException($"아이템 정의의 equipSlot이 잘못됐습니다: {itemId} -> {slot}");
            }

            _equipSlotByItemId = definitions.ToDictionary(pair => pair.Key, pair => pair.Value.EquipSlot);
        }

        // itemId를 equipSlot에 장착할 수 있는지(정의된 장착 슬롯이 정확히 그 슬롯인지).
        public static bool CanEquip(string itemId, string equipSlot)
        {
            if (_equipSlotByItemId is null)
                throw new InvalidOperationException("ItemEquipSlotCatalog.EnsureLoaded가 호출되지 않았습니다.");

            return _equipSlotByItemId.TryGetValue(itemId, out string? slot) && slot == equipSlot;
        }

        private sealed class ItemDefinitionEntry
        {
            public string? EquipSlot { get; init; }
        }
    }
}
