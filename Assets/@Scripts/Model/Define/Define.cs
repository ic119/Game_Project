public enum AddressKey
{
    UI_LoginScene,
    UI_AlarmPopup,
    PlayerPrefab,
    Test_Map
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
