using JJORY.Module;
using JJORY.Util;
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

            }

            AddressableController.Instance.LoadPrefabAddressFromHashSet();
        }

        private void Start()
        {
            if (AddressableController.Instance != null)
            {
                StartCoroutine(AddressableController.Instance.InstantiateAsset(AddressKey.UI_LoginScene.ToString(), gameObject));
            }
        }

        private void OnDestroy() 
        {
            Utils.CreateLogMessage<LoginSceneController>("LoginScene 제거");
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_LoginScene.ToString());
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_AlarmPopup.ToString());
        }
        #endregion

        #region Method
        #endregion
    }
}
