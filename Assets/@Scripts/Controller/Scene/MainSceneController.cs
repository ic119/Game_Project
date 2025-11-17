using JJORY.Module;
using JJORY.Util;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace JJORY.Scene
{
    public class MainSceneController : MonoBehaviour
    {
        #region Variable
        [Header("Controller Container")]
        public GameObject playerControllerModule;
        #endregion

        #region LifeCycle
        private void Start()
        {
            Utils.CreateLogMessage<MainSceneController>("Main Scene Load Complete!");

            StartCoroutine(AddressableController.Instance.InstantiateAsset(AddressKey.UI_CharacterInfoPopup.ToString(), gameObject));
        }
        #endregion

        #region Method
        #endregion
    }
}