using JJORY.Module;
using JJORY.Util;
using UnityEngine;

namespace JJORY.Scene
{
    public class MainSceneController : MonoBehaviour
    {
        #region Variable
        [Header("Controller Container")]
        public GameObject examplePlayerModule;
        #endregion

        #region LifeCycle
        private void Start()
        {
            Utils.CreateLogMessage<MainSceneController>("Main Scene Load Complete!");
            StartCoroutine(AddressableController.Instance.InstantiateAsset(AddressKey.UI_CharacterInfoPopup.ToString(), gameObject));

            if (GameManager.Instance != null )
            {
                GameManager.Instance.GenerateMaps(AddressKey.Beginner_Village.ToString(), gameObject);
            }
        }
        #endregion

        #region Method
        
        #endregion
    }
}