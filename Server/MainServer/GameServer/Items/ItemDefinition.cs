namespace GameServer.Items
{
    // 아이템 하나의 설계 데이터(이름/최대 스택/전투 보너스 등). 이 서버(GameServer)는 인벤토리를 직접
    // 소유하지 않으므로(DB는 MainServer 쪽), DropTableCatalog가 참조하는 ItemId가 실존하는지 검증하고,
    // Combat.CombatStatCalculator가 장착 아이템의 공격력/방어력 보너스를 조회하는 용도로 쓰인다.
    // Client Assets/@Scripts/Models/ItemData.cs(bonusAttackPower/bonusDefense)와 값이 반드시 같아야 한다 -
    // 장비 아이템을 추가할 때 ItemDatabaseSO(클라이언트)와 이 파일이 참조하는 ItemDefinitions.json을
    // 함께 갱신해야 한다.
    public class ItemDefinition
    {
        public string Name { get; init; } = string.Empty;
        public int MaxStack { get; init; } = 99;
        public int BonusAttackPower { get; init; } = 0;
        public int BonusDefense { get; init; } = 0;
    }
}
