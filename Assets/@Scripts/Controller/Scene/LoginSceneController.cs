using JJORY.Controller.Base;
using JJORY.Module;
using JJORY.Util;
using System.Collections;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace JJORY.Scene.Login
{
    public class LoginSceneController : BaseSceneController
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
        }

        private void Start()
        {
            StartCoroutine(InstantiateAsset(AddressKey.UI_LoginScene.ToString(), gameObject));
        }

        private void OnDestroy() 
        {
            Utils.CreateLogMessage<LoginSceneController>("LoginScene 제거");
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_LoginScene.ToString());
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_AlarmPopup.ToString());
        }
        #endregion

        #region Method
        protected override IEnumerator InstantiateAsset(string _key, GameObject _parent)
        {
            return base.InstantiateAsset(_key, _parent);
        }
        #endregion
    }
}