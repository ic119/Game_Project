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
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.UI_LoginScene.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.UI_AlarmPopup.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.UI_CharacterInfoPopup.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.StatusInfoItem.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.Player_Admin.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.Beginner_Village.ToString());
        }

        private void Start()
        {
            StartCoroutine(AddressableController.Instance.InstantiateAsset(AddressKey.UI_LoginScene.ToString(), gameObject));
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