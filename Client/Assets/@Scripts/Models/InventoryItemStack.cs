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

    public InventoryItemStack(string _itemId, int _count)
    {
        itemId = _itemId;
        count = _count;
    }
}
