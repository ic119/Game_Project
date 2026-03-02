public enum AddressKey
{
    UI_LoginScene,
    UI_AlarmPopup,
    PlayerPrefab,
    BeginnerVillage
}

/// <summary>
/// 플레이어 이동 상태
/// </summary>
public enum PlayerMoveState
{
    Idle = 0,
    Walk,
    Run,
    Jump
}

/// <summary>
/// 플레이어 공격 콤보
/// </summary>
public enum PlayerAttackState
{
    Attack01 = 0,
    Attack02 = 1,
    Attack03 = 2,
    AttackFinish = 3
}

/// <summary>
/// 플레이어 무기 타입
/// </summary>
public enum PlayerWeaponType
{
    Dagger,
    DualDagger,
    OneHandedSword,
    TwoHandedSword
}

/// <summary>
/// Player의 현재 상태에 대한 타입
/// </summary>
public enum PlayerState
{
    Live,
    Dead,
    Hit,
    Attack
}

public enum ItemGrade
{
    Common,     // 일반 등급 - 흰색
    Rare,       // 레어 등급 - 주황색
    Epic,       // 에픽 등급 - 파랑색
    Elite,      // 엘리트 등급 - 남색
    Legendary   // 레전더리 등급 - 노랑색
}
