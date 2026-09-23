using System;

/// <summary>
/// 인벤토리 한 칸에 실제로 들어있는 아이템의 인스턴스 데이터(어떤 아이템을 몇 개 들고 있는가).
/// 이름/설명/아이콘 등 아이템 자체의 정적 정의는 ItemData(ItemDatabaseSO)에서 itemId로 조회한다.
/// </summary>
[Serializable]
public class InventoryItemStack
{
    public string itemId;
    public int count;

    /// <summary>
    /// 이 스택이 현재 장착 중인 장비 슬롯(EquipmentSlotType 이름, 예: "Weapon"). null/빈 문자열이면 미장착 상태로
    /// 일반 인벤토리 그리드에 표시된다. 서버 CharacterItem.EquipSlot과 값을 그대로 맞춘다.
    /// </summary>
    public string equipSlot;

    public InventoryItemStack(string _itemId, int _count, string _equipSlot = null)
    {
        itemId = _itemId;
        count = _count;
        equipSlot = _equipSlot;
    }
}
