namespace Incheol.Models.Define
{
    // Inspector/ScriptableObject에 정수값으로 직렬화되므로, 선언 순서를 바꾸거나 중간에 값을 끼워 넣어도
    // 기존에 저장된 데이터가 다른 멤버를 가리키게 되지 않도록 값을 명시적으로 고정한다.
    // 새 멤버 추가 시 항상 끝에 사용하지 않은 번호로 추가할 것.
    public enum AddressableAssetKey
    {
        None = 0,
        UI_LoginScene = 1,
        UI_CharacterInfoViewPopup = 2,
        UI_AlarmPopup = 3,
        UI_Inventory = 4,
        UI_LoadingBarView = 5,
        UI_LobbyScene = 6,
        BasicCharacter = 7,
        UI_CharacterListItem = 8,
        UI_GameScene = 9,
        UI_InventorySlot = 11,
        Floor001 = 12,
        OneHandedAttack01 = 13,
        SpearAttack01 = 14,
        TwoHandedAttack01 = 15,
        OneHandedHit01 = 16,
        SpearHit01 = 17,
        TwoHandedHit01 = 18,
        Dash01 = 20,
        LevelUp01 = 21,
        HpPotion01 = 22,
        ChestDespawn01 = 23,
        Dodge01 = 24,
        Dungeon = 25,
        PortalGate = 26,
        BossTelegraph01 = 27,

        // 완드(원거리) 공격 이펙트: 시전(지팡이 끝) / 날아가는 투사체(반복 재생) / 명중.
        WandCast01 = 28,
        WandBolt01 = 29,
        WandHit01 = 30,

        // 액티브 스킬 이펙트(SkillVfxDatabaseSO). 이름의 앞부분이 종류다 - Slash는 시전자 곁에서 휘두르는 궤적, Area는 범위 위치에 터지는 바닥/하늘 공격.
        SkillSlashMagicOnce = 31,
        SkillSlashMagicLong = 32,
        SkillSlashFireLong = 33,
        SkillSlashStoneOnce = 34,
        SkillSlashStoneLong = 35,
        SkillSlashSnowOnce = 36,
        SkillSlashSnowLong = 37,
        SkillAreaStoneLine = 38,
        SkillAreaStoneCircle = 39,
        SkillAreaLightningCircle = 40,
        SkillAreaIceLine = 41,
        SkillHitFire = 42,
        SkillHitWind = 43,
        SkillNovaBlue = 44,
        SkillExplosionEarth = 45,
        SkillDustFloorHit = 46,
        SkillMagicCircle = 47
    }

    /// <summary>
    /// 공격 모션 알림(Game_AttackAnimationRequest/Broadcast)에 실리는 투사체 목표의 종류. 값은 패킷의 TargetType 바이트다 -
    /// 서버는 해석하지 않고 그대로 중계하므로(연출용) 바꾸면 클라이언트끼리만 맞추면 된다.
    /// </summary>
    public enum AttackTargetKind : byte
    {
        None = 0,
        Monster = 1,
        Player = 2
    }

    /// <summary>
    /// 보스 스킬 종류. 값은 서버 GameServer.Monsters.BossSkillType과 같다(Game_BossSkillTelegraphBroadcast/Game_BossSkillEndBroadcast의
    /// SkillType 바이트가 이 값이다) - 바꾸려면 서버와 함께 바꿔야 한다.
    /// </summary>
    public enum BossSkillType : byte
    {
        None = 0,

        // 보스 주변 원형 범위 공격
        AreaSlam = 1,

        // 예고된 직선 방향으로 달려가는 돌진
        Charge = 2,

        // 예고된 자리에 하수인 소환
        Summon = 3
    }

    /// <summary>
    /// 몬스터 생성 및 스폰 설정 시 사용하기 위한 몬스터 관련 Key만 관리하는 Enum형 변수
    /// </summary>
    public enum MonsterType
    {
        None = 0,
        RedMushroom = 1,
        Spider = 2,
        Orc = 3,
        Werewolf = 4,
        Golem = 5,
        ChestMonster = 6,
        Specter = 7,
        RatAssassin = 8,
        BlackKnight = 9
    }

    /// <summary>
    /// 보물상자 후보 지점/등급별 개수 설정에서 고르는 드롭 테이블 키. 멤버 이름이 서버 Drops/DropTables.json의
    /// 키와 글자 그대로 일치해야 한다(MapDataExporter가 ToString()으로 내보낸다) -
    /// 새 멤버를 추가하려면 DropTables.json에 같은 이름의 항목을 먼저 만들 것.
    /// </summary>
    public enum ChestLootTableKey
    {
        None = 0,
        TreasureChestBasic = 1,
        TreasureChestHidden = 2,
        TreasureChestRare = 3
    }
}

/// <summary>
/// 무기 오브젝트 이름 접두사와 매핑되는 무기 분류.
/// OH(One-Handed) = 한손무기류, TH(Two-Handed) = 두손무기류, Wand = 원드류, Spear = 창류.
///
/// 값은 애니메이터 WeaponIndex(BlendTree 임계값)와 직렬화 데이터(ItemDatabaseSO/WeaponVfxDatabaseSO)가 정수 그대로 쓰므로
/// 명시적으로 고정한다. 3은 예전에 Shield(방패)가 쓰던 값이라 제거한 뒤에도 비워 둔다 - 다른 값으로 재사용하거나
/// Wand/Spear의 번호를 당기지 말 것(BlendTree의 Wand/Spear 클립 매핑이 어긋난다).
/// </summary>
public enum WeaponType
{
    None = 0,
    OneHanded = 1,
    TwoHanded = 2,
    // 3 = (제거됨) Shield
    Wand = 4,
    Spear = 5
}

public enum ItemType
{
    Eqiupment,  // 장비 아이템
    Potion,     // 물약 아이템
    General     // 기타 아이템
}

/// <summary>
/// 장비 아이템(ItemType.Eqiupment)이 장착되는 슬롯 종류.
/// UI_InventorySlot.InventorySlotType의 장비 관련 값(EquipmentWeapon 등)과 1:1로 대응한다.
///
/// ItemDatabaseSO에 정수값으로 직렬화되므로 값을 고정한다. 4는 예전에 Boots(신발)가 쓰던 값이라 제거한 뒤에도 비워 두고
/// Accessory의 값(5)을 당기지 않는다(서버 장착 슬롯 문자열도 "Boots"를 더 이상 받지 않는다).
/// </summary>
public enum EquipmentSlotType
{
    None = 0,
    Weapon = 1,
    Armor = 2,
    Helmet = 3,
    // 4 = (제거됨) Boots
    Accessory = 5
}

// ItemDatabaseSO(ScriptableObject)에 아이템 데이터와 함께 정수값으로 직렬화되므로,
// 선언 순서를 바꾸지 말고 새 등급은 항상 끝에 추가할 것.
/// <summary>
/// 아이템 등급. 인벤토리 슬롯의 GradeBorder 색상(ItemGradeUtils.GetGradeColor)이 이 값으로 결정된다.
/// </summary>
public enum ItemGrade
{
    Common = 0,     // 일반
    Rare = 1,       // 희귀
    Epic = 2,       // 영웅
    Legendary = 3   // 전설
}
