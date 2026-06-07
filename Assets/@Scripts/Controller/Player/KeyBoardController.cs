using JJORY.Util;
using UnityEngine;

/// <summary>
/// 게임 내 유저의 키보드 입력을 처리합니다.
/// </summary>
public class KeyBoardController : MonoBehaviour
{
    #region Variable
    private bool isInventoryActive = false;
    #endregion

    #region LifeCycle
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventory();
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
    #endregion
}
