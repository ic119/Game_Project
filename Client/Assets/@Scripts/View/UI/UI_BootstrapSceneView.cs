using UnityEngine;

namespace Incheol.View.UI
{
    public class UI_BootstrapSceneView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private UI_ProgressBarView progressBarView;
        #endregion

        #region LifeCycle
        #endregion

        #region Method
        /// <summary>
        /// Bootstrap 진행률(0.0f~1.0f)을 UI_ProgressBarView에 반영한다.
        /// </summary>
        public void UpdateProgress(float progress)
        {
            if (progressBarView == null)
            {
                return;
            }

            progressBarView.SetProgress(progress);
        }
        #endregion
    }
}
