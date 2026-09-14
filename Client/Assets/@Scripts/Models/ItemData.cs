using System;
using Incheol.Models.Define;
using UnityEngine;

/// <summary>
/// 아이템 하나의 정적 정의(디자인 데이터). ItemDatabaseSO에 리스트로 등록되어 itemId로 조회된다.
/// 인벤토리 슬롯에 실제로 몇 개 들어있는지 같은 "인스턴스" 정보는 InventoryItemStack이 별도로 들고,
/// 이 클래스는 "이 아이템이 무엇인가"만 정의한다.
/// </summary>
[Serializable]
public class ItemData
{
    [Tooltip("아이템 고유 식별자. UI_InventorySlot.ItemId, InventoryItemStack.itemId와 이 값으로 매칭된다.")]
    public string itemId;

    public string itemName;

    [TextArea]
    public string description;

    public ItemType itemType;

    [Tooltip("itemType이 Eqiupment일 때만 의미가 있다.")]
    public EquipmentSlotType equipSlotType = EquipmentSlotType.None;

    [Tooltip("equipSlotType이 Weapon일 때만 의미가 있다(한손/두손/방패/완드/창).")]
    public WeaponType weaponType = WeaponType.None;

    public Sprite icon;

    [Tooltip("슬롯 등급 테두리 색상.")]
    public Color gradeColor = Color.white;

    [Min(1)]
    [Tooltip("한 슬롯에 중첩 가능한 최대 개수. 장비류는 보통 1.")]
    public int maxStackCount = 1;
}
