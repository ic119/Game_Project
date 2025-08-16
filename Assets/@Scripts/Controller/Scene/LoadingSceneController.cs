using JJORY.Module;
using JJORY.Util;
using TMPro;
using UnityEngine;

namespace JJORY.Scene
{
    public class LoadingSceneController : MonoBehaviour
    {
        #region Variable
        [SerializeField] TextMeshProUGUI loadingText;
        #endregion

        #region LifeCycle
        private void Start()
        {
            Utils.CreateLogMessage<LoadingSceneController>("Loading Scene Load Complete!");
        }
        private void Update()
        {
            float progress = SceneLoadController.Instance.cur_LoadProgress;
            progress *= 100.0f;
            int nProgress = (int)progress;
            loadingText.text = $"{nProgress}%";
        }
        #endregion
    }
}