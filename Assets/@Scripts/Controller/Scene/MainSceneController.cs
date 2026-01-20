using JJORY.Module;
using JJORY.Util;
using UnityEngine;

namespace JJORY.Scene
{
    public class MainSceneController : MonoBehaviour
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Start()
        {
            Utils.CreateLogMessage<MainSceneController>("Main Scene Load Complete!");
            StartCoroutine(AddressableController.Instance.InstantiateAsset(AddressKey.UI_CharacterInfoPopup.ToString(), gameObject));

            if (GameManager.Instance != null )
            {
                GameManager.Instance.GenerateMaps(AddressKey.Test_Map.ToString(), gameObject);
            }
        }
        #endregion

        #region Method
        
        #endregion
    }
}