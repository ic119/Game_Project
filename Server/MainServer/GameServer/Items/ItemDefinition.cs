namespace GameServer.Items
{
    // 아이템 하나의 설계 데이터(이름/최대 스택/전투 보너스 등). 이 서버(GameServer)는 인벤토리를 직접
    // 소유하지 않으므로(DB는 MainServer 쪽), DropTableCatalog가 참조하는 ItemId가 실존하는지 검증하고,
    // Combat.CombatStatCalculator가 장착 아이템의 공격력/방어력 보너스를, ClientSession이 물약 회복량을 조회하는 용도로 쓰인다.
    // Client Assets/@Scripts/Models/ItemData.cs(bonusAttackPower/bonusDefense/healPercent)와 값이 반드시 같아야 한다 -
    // 장비/물약 아이템을 추가할 때 ItemDatabaseSO(클라이언트)와 이 파일이 참조하는 ItemDefinitions.json을
    // 함께 갱신해야 한다.
    public class ItemDefinition
    {
        public string Name { get; init; } = string.Empty;
        public int MaxStack { get; init; } = 99;
        public int BonusAttackPower { get; init; } = 0;
        public int BonusDefense { get; init; } = 0;

        // 사용 시 최대 체력 대비 회복 비율(%). 0이면 회복 아이템이 아니다(Game_UseItemRequest 거부).
        public int HealPercent { get; init; } = 0;

        // 이 물약을 쓴 뒤 다음 물약(종류 무관 - 모든 물약이 하나의 대기시간을 공유한다)을 쓸 수 있기까지의 시간(초).
        // 회복 속도 상한(HealPercent / UseCooldownSeconds, 권장 5%/초)을 정하는 값이다 - 없으면 물약을 0.3초마다 연속으로
        // 써서 한 전투 안에서 체력을 사실상 무한히 회복할 수 있다. 0이면 대기시간 없음.
        // 클라이언트 ItemData.useCooldownSeconds와 같은 값이어야 한다(ItemDefinitionValidator.Generate가 동기화).
        public float UseCooldownSeconds { get; init; } = 0f;

        // 장착 가능한 슬롯("Weapon"/"Armor"/"Helmet"/"Accessory", 클라이언트 ItemData.equipSlotType과 같은 이름).
        // 없으면 장비가 아니다. GameServer는 쓰지 않고, MainServer가 같은 파일을 읽어 장착 요청을 검증한다(ItemEquipSlotCatalog).
        public string? EquipSlot { get; init; }

        // "Common"/"Rare"/"Epic"/"Legendary" 중 하나(클라이언트 ItemData.itemGrade와 같은 이름). DropTableEntry.Grade가
        // 이 값으로 무작위 드롭 풀을 구성한다(ItemCatalog.TryGetRandomByGrade 참고).
        public string? Grade { get; init; }
    }
}
