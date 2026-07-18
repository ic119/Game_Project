using JJORY.Module;
using JJORY.Util;
using System.Collections;
using UnityEngine;

namespace JJORY.Scene.Login
{
    public class LoginSceneController : MonoBehaviour
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (AddressableController.Instance != null)
            {
                AddressableController.Instance.AddKeyHashSet(AddressKey.UI_LoginScene.ToString());
                AddressableController.Instance.AddKeyHashSet(AddressKey.UI_AlarmPopup.ToString());
                AddressableController.Instance.AddKeyHashSet(AddressKey.BeginnerVillage.ToString());
                AddressableController.Instance.AddKeyHashSet(AddressKey.PlayerPrefab.ToString());
                AddressableController.Instance.AddKeyHashSet(AddressKey.UI_MainScene.ToString());
                AddressableController.Instance.AddKeyHashSet(AddressKey.UI_InventoryViewPopup.ToString());
                AddressableController.Instance.AddKeyHashSet(AddressKey.UI_CharacterInfoVIewPopup.ToString());

            }

            AddressableController.Instance.LoadPrefabAddressFromHashSet();
        }

        private IEnumerator Start()
        {
            if (AddressableController.Instance != null)
            {
                yield return AddressableController.Instance.InstantiateAsset(
                    AddressKey.UI_LoginScene.ToString(),
                    gameObject);
            }
        }

        private void OnDestroy() 
        {
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_LoginScene.ToString());
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_AlarmPopup.ToString());
        }
        #endregion

        #region Method
        #endregion
    }
}
