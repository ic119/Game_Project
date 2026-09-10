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
        UI_InventoryViewPopup = 4,
        UI_LoadingBarView = 5,
        UI_LobbyScene = 6,
        BasicCharacter = 7,
        UI_CharacterListItem = 8,
        UI_GameScene = 9,
        Farm = 10
    }
}

public enum PlayerMoveState
{
    IsIdle,
    IsMove,
    IsDash,
    IsJump
}

/// <summary>
/// 무기 오브젝트 이름 접두사와 매핑되는 무기 분류.
/// OH(One-Handed) = 한손무기류, TH(Two-Handed) = 두손무기류, Shield = 방패류, Wand = 원드류, Spear = 창류.
/// </summary>
public enum WeaponType
{
    None,
    OneHanded,
    TwoHanded,
    Shield,
    Wand,
    Spear
}
