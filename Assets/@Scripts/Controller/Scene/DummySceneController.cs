using Incheol.Module;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Scene.Dummy
{
    public class DummySceneController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private UI_ProgressBarView progressBarView;
        #endregion

        #region LifeCycle
        private async void Start()
        {
            if (progressBarView != null)
            {
                progressBarView.SetTitle("Loading...");
                progressBarView.SetProgress(0f);
            }

            SceneLoadController.Instance.OnLoadProgressChanged += OnLoadProgressChanged;

            try
            {
                bool isSceneModelLoaded = false;
                SceneLoadController.Instance.Init(() =>
                {
                    isSceneModelLoaded = true;
                });

                while (!isSceneModelLoaded)
                {
                    await Awaitable.NextFrameAsync();
                }

                await SceneLoadController.Instance.LoadInitialSceneAsync("Login");
            }
            finally
            {
                SceneLoadController.Instance.OnLoadProgressChanged -= OnLoadProgressChanged;
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// SceneLoadController의 로딩 스텝이 완료될 때마다 호출되어 ProgressBar를 갱신한다.
        /// 100%가 되는 시점은 곧 대상 씬 로드와 DummyScene 언로드가 끝난 시점이다.
        /// </summary>
        private void OnLoadProgressChanged(float _progress)
        {
            progressBarView?.SetProgress(_progress);
        }
        #endregion
    }
}
