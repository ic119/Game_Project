namespace MainServer.CharacterServer.DTOs
{
    public record CharacterResponse(
        long _id,
        string _nickname,
        int _hairIndex,
        int _eyeIndex,
        int _mouthIndex,
        int _str,
        int _agi,
        int _intel,
        int _level,
        int _exp,
        long _gold,
        IReadOnlyList<CharacterItemResponse> _items,
        DateTime? _lastLoginAt,
        DateTime _createdAt);

    // 캐릭터가 보유한 아이템 한 스택(CharacterItem 엔티티를 그대로 노출). ItemId가 가리키는 이름/아이콘 등
    // 정의는 클라이언트의 ItemDatabaseSO가 조회하므로 여기서는 수량만 함께 내려준다.
    // _equipSlot은 CharacterItem.EquipSlot 그대로("Weapon"/"Armor"/"Helmet"/"Boots"/"Accessory" 또는 null=미장착).
    public record CharacterItemResponse(string _itemId, int _qty, string? _equipSlot);
}
