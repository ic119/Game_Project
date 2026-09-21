namespace MainServer.CharacterServer.Entities
{
    // 캐릭터가 보유한 아이템 한 스택. 슬롯 인덱스 없이 (CharacterId, ItemId) 단위로 수량만 누적하는
    // 스택형 인벤토리다 - 슬롯 배치/드래그앤드롭 UI 요구사항이 아직 없는 상태에서 슬롯 관리까지
    // 미리 설계하지 않는다. ItemId가 가리키는 이름/최대 스택 등 정의는 GameServer의 Items/ItemDefinitions.json
    // (설계 데이터)에 있고, 여기 DB에는 "누가 몇 개 들고 있는지"만 저장한다.
    public class CharacterItem
    {
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public string ItemId { get; set; } = null!;
        public int Quantity { get; set; }
        public Character Character { get; set; } = null!;
    }
}
