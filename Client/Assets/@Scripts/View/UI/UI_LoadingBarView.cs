using UnityEngine;

namespace Incheol.View.UI
{
    public class UI_LoadingBarView : MonoBehaviour
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

        /// <summary>
        /// 부트스트랩 단계가 무엇을 수행 중/완료했는지를 UI_ProgressBarView의 타이틀 텍스트로 표시한다.
        /// </summary>
        public void UpdateTitle(string title)
        {
            if (progressBarView == null)
            {
                return;
            }

            progressBarView.SetTitle(title);
        }

        #endregion
    }
}
