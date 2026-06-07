using JJORY.Util;
using UnityEngine;

/// <summary>
/// 게임 내 유저의 키보드 입력을 처리합니다.
/// </summary>
public class KeyBoardController : MonoBehaviour
{
    #region Variable
    private bool isInventoryActive = false;
    private bool isStatsActive = false;
    private bool isQuestActive = false;
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
        isInventoryActive = !isInventoryActive;

        if (isInventoryActive)
        {
            Utils.CreateLogMessage<KeyBoardController>("인벤토리 UI 활성화");
        }
        else
        {
            Utils.CreateLogMessage<KeyBoardController>("인벤토리 UI 비활성화");
        }
    }

    private void ToggleStats()
    {
        isStatsActive = !isStatsActive;

        if (isStatsActive)
        {
            Utils.CreateLogMessage<KeyBoardController>("스탯 UI 활성화");
        }
        else
        {
            Utils.CreateLogMessage<KeyBoardController>("스탯 UI 비활성화");
        }
    }

    private void ToggleQuest()
    {
        isQuestActive = !isQuestActive;

        if (isQuestActive)
        {
            Utils.CreateLogMessage<KeyBoardController>("퀘스트 UI 활성화");
        }
        else
        {
            Utils.CreateLogMessage<KeyBoardController>("퀘스트 UI 비활성화");
        }
    }
    #endregion
}
