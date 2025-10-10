using JJORY.Controller.Base;
using JJORY.Module;
using JJORY.Util;
using System.Collections;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace JJORY.Scene
{
    public class MainSceneController : BaseSceneController
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Start()
        {
            Utils.CreateLogMessage<MainSceneController>("Main Scene Load Complete!");

            StartCoroutine(InstantiateAsset(AddressKey.UI_CharacterInfoPopup.ToString(), gameObject));
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