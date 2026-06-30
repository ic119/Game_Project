using JJORY.Util;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            inventoryPopup = FindInventoryPopup();
        }

        if (inventoryPopup == null)
        {
            Utils.CreateLogMessage<KeyBoardController>("UI_InventoryViewPopup을 찾지 못했습니다.");
            return;
        }

        bool isActive = !inventoryPopup.activeSelf;
        inventoryPopup.SetActive(isActive);

        if (isActive)
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
        if (characterInfoPopup == null)
        {
            characterInfoPopup = FindCharacterInfoPopup();
        }

        if (characterInfoPopup == null)
        {
            Utils.CreateLogMessage<KeyBoardController>("UI_CharacterInfoVIewPopup을 찾지 못했습니다.");
            return;
        }

        bool isActive = !characterInfoPopup.activeSelf;
        characterInfoPopup.SetActive(isActive);

        if (isActive)
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

    private GameObject FindInventoryPopup()
    {
        UI_InventoryViewPopupController[] controllers = FindObjectsByType<UI_InventoryViewPopupController>(FindObjectsInactive.Include);

        if (controllers.Length > 0)
        {
            return controllers[0].gameObject;
        }

        return FindUIPopupByName(AddressKey.UI_InventoryViewPopup.ToString());
    }

    private GameObject FindCharacterInfoPopup()
    {
        return FindUIPopupByName(AddressKey.UI_CharacterInfoVIewPopup.ToString());
    }

    private GameObject FindUIPopupByName(string _popupName)
    {
        GameObject mainSceneRoot = GameObject.Find("@MainScene");
        if (mainSceneRoot != null)
        {
            Transform found = FindInChildren(mainSceneRoot.transform, _popupName);
            if (found != null)
            {
                return found.gameObject;
            }
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
            {
                continue;
            }

            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int j = 0; j < rootObjects.Length; j++)
            {
                Transform found = FindInChildren(rootObjects[j].transform, _popupName);
                if (found != null)
                {
                    return found.gameObject;
                }
            }
        }

        return null;
    }

    private bool IsNameMatch(string _objectName, string _targetName)
    {
        if (string.IsNullOrEmpty(_objectName) || string.IsNullOrEmpty(_targetName))
        {
            return false;
        }

        return _objectName == _targetName || _objectName.StartsWith($"{_targetName}(Clone)");
    }

    private Transform FindInChildren(Transform _parent, string _name)
    {
        if (IsNameMatch(_parent.name, _name))
        {
            return _parent;
        }

        for (int i = 0; i < _parent.childCount; i++)
        {
            Transform found = FindInChildren(_parent.GetChild(i), _name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
    #endregion
}
