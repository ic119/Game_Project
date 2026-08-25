using UnityEngine;

namespace Incheol.Models.Define
{
    public class DefineRule
    {
        public static readonly string[] ADDRESSABLE_LABEL = { "default" };
    }

    public enum AddressableAssetKey
    {
        None,
        BeginnerVillage,
        PlayerPrefab,
        UI_LoginScene,
        UI_CharacterInfoViewPopup,
        UI_AlarmPopup,
        UI_InventoryViewPopup,
        UI_LoadingBarView,
        UI_LobbyScene
    }
}
