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
    [Tooltip("아이템 고유 식별자. \"카테고리 접두사_스네이크케이스\" 규칙으로 짓는다 - " +
        "무기 weapon_, 방어구 armor_, 투구 helmet_, 신발 boots_, 장신구 accessory_, 물약 potion_, 재료/기타 material_. " +
        "예) weapon_sword_iron, potion_health_small, material_stone. " +
        "UI_InventorySlot.ItemId, InventoryItemStack.itemId와 이 값으로 매칭되므로 한 번 정하면 바꾸지 않는다.")]
    public string itemId;

    public string itemName;

    [TextArea]
    public string description;

    public ItemType itemType;

    [Tooltip("itemType이 Eqiupment일 때만 의미가 있다.")]
    public EquipmentSlotType equipSlotType = EquipmentSlotType.None;

    [Tooltip("equipSlotType이 Weapon일 때만 의미가 있다(한손/두손/방패/완드/창).")]
    public WeaponType weaponType = WeaponType.None;

    [Tooltip("장착 시 활성화할 메시 오브젝트 이름. PlayerCharacterModel의 EquipmentController가 관리하는 장비 컨테이너" +
        "(rightArmEqiupment/leftArmEqiupment/bodyEqiupment 등) 하위 자식 오브젝트 이름과 정확히 일치해야 한다. " +
        "예) OHS03_Sword, Shield01, Body05. equipSlotType이 None이면 비워둔다.")]
    public string equipVisualName;

    public Sprite icon;

    [Tooltip("아이템 등급. 인벤토리 슬롯의 GradeBorder 색상이 이 값에 따라 자동으로 정해진다(ItemGradeUtils.GetGradeColor).")]
    public ItemGrade itemGrade = ItemGrade.Common;

    [Min(1)]
    [Tooltip("한 슬롯에 중첩 가능한 최대 개수. 장비류는 보통 1.")]
    public int maxStackCount = 1;

    [Tooltip("itemType이 Eqiupment일 때만 의미가 있다. 장착 시 CombatStatComponent.AttackPower에 더해지는 보너스.")]
    public int bonusAttackPower = 0;

    [Tooltip("itemType이 Eqiupment일 때만 의미가 있다. 장착 시 CombatStatComponent.Defense에 더해지는 보너스.")]
    public int bonusDefense = 0;

    [Range(0, 100)]
    [Tooltip("itemType이 Potion일 때만 의미가 있다. 사용 시 최대체력의 이 비율(%)만큼 현재체력을 회복시킨다" +
        "(HealthComponent.HealByPercent). 예) potion_health_small=10, potion_health_medium=30, potion_health_large=50.")]
    public int healPercent = 0;
}
