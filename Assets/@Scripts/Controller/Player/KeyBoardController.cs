using Incheol.Module;
using Incheol.Util;
using UnityEngine;

/// <summary>
/// 게임 내 유저의 키보드 입력을 처리합니다.
/// </summary>
public class KeyBoardController : MonoBehaviour
{
    #region Variable
    private bool isQuestActive = false;

    private GameObject inventoryPopup;
    private GameObject characterInfoPopup;
    #endregion

    #region LifeCycle
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventory();
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            ToggleStats();
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            ToggleQuest();
        }
    }
    #endregion

    #region Method
    private void ToggleInventory()
    {
        if (inventoryPopup == null)
        {
            inventoryPopup = RuntimeObjectRegistry.Instance.Get(AddressKey.UI_InventoryViewPopup.ToString());
        }

        if (inventoryPopup == null)
        {
            Utils.CreateLogMessage<KeyBoardController>("UI_InventoryViewPopup이 Registry에 없습니다.");
            return;
        }

        bool isActive = !inventoryPopup.activeSelf;
        inventoryPopup.SetActive(isActive);

        Utils.CreateLogMessage<KeyBoardController>(isActive ? "인벤토리 UI 활성화" : "인벤토리 UI 비활성화");
    }

    private void ToggleStats()
    {
        if (characterInfoPopup == null)
        {
            characterInfoPopup = RuntimeObjectRegistry.Instance.Get(AddressKey.UI_CharacterInfoVIewPopup.ToString());
        }

        if (characterInfoPopup == null)
        {
            Utils.CreateLogMessage<KeyBoardController>("UI_CharacterInfoVIewPopup이 Registry에 없습니다.");
            return;
        }

        bool isActive = !characterInfoPopup.activeSelf;
        characterInfoPopup.SetActive(isActive);

        Utils.CreateLogMessage<KeyBoardController>(isActive ? "스탯 UI 활성화" : "스탯 UI 비활성화");
    }

    private void ToggleQuest()
    {
        isQuestActive = !isQuestActive;
        Utils.CreateLogMessage<KeyBoardController>(isQuestActive ? "퀘스트 UI 활성화" : "퀘스트 UI 비활성화");
    }
    #endregion
}
