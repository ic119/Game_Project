using JJORY.Define;
using JJORY.Module;
using JJORY.Scene.Base;
using UnityEngine;

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

        }

        private void Start()
        {
            StartCoroutine(InstantiateAsset(AddressKey.UI_LoginScene.ToString(), gameObject));
        }
        #endregion
    }
}