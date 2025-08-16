using JJORY.Util;
using Unity.VisualScripting;
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
        }
        #endregion
    }
}