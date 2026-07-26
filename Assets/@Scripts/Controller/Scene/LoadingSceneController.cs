using Incheol.Module;
using Incheol.Util;
using TMPro;
using UnityEngine;

namespace Incheol.Scene
{
    public class LoadingSceneController : MonoBehaviour
    {
        #region Variable
        [SerializeField] TextMeshProUGUI loadingText;
        #endregion

        #region LifeCycle
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
