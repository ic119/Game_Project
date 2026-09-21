namespace GameServer.Items
{
    // 아이템 하나의 설계 데이터(이름/최대 스택 등). 이 서버(GameServer)는 인벤토리를 직접 소유하지
    // 않으므로(DB는 MainServer 쪽), 여기서는 DropTableCatalog가 참조하는 ItemId가 실존하는지
    // 검증하는 용도로만 쓰인다.
    public class ItemDefinition
    {
        public string Name { get; init; } = string.Empty;
        public int MaxStack { get; init; } = 99;
    }
}
