namespace MainServer.CharacterServer.DTOs
{
    // 인벤토리에서 보유 중인 장비 아이템을 장착할 때 쓴다. _equipSlot은 클라이언트 EquipmentSlotType 이름
    // ("Weapon"/"Armor"/"Helmet"/"Boots"/"Accessory") 문자열이다.
    public record EquipItemRequest(string _itemId, string _equipSlot);
}
