using Incheol.Model;
using Incheol.Module;
using Incheol.Util;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Incheol.Scene.Dummy
{
    public class DummySceneController : MonoBehaviour
    {
        #region Type
        /// <summary>
        /// Scene 전환 전 준비 작업 큐에 들어가는 개별 작업 단위. 완료되면 진행률이 갱신된다.
        /// </summary>
        private class LoadStep
        {
            public string Name { get; }
            public Func<Awaitable> Action { get; }

            public LoadStep(string _name, Func<Awaitable> _action)
            {
                Name = _name;
                Action = _action;
            }
        }
        #endregion

        #region Variable
        [Header("UI 변수")]
        [SerializeField] private UI_ProgressBarView progressBarView;

        [Header("최초 진입 대상 Scene Tag")]
        [SerializeField] private string targetSceneTag = "Login";

        private int loadTotalSteps;
        private int loadCurrentStep;
        #endregion

        #region LifeCycle
        private async void Start()
        {
            if (progressBarView != null)
            {
                progressBarView.SetTitle("Loading...");
                progressBarView.SetProgress(0f);
            }

            try
            {
                Queue<LoadStep> loadStepQueue = BuildPreTransitionStepQueue();
                ResetLoadProgress(loadStepQueue.Count);

                while (loadStepQueue.Count > 0)
                {
                    LoadStep step = loadStepQueue.Dequeue();
                    await step.Action();
                    CompleteLoadStep();
                }

                await TransitionToTargetSceneAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// Scene 전환 전 필요한 준비 작업(에셋 로딩 등)을 순서대로 Queue에 담는다.
        /// 여기서 담당하는 진행률이 100%가 되어야 SceneLoadController에 실제 Scene 전환을 요청한다.
        /// </summary>
        private Queue<LoadStep> BuildPreTransitionStepQueue()
        {
            Queue<LoadStep> stepQueue = new Queue<LoadStep>();
            stepQueue.Enqueue(new LoadStep("SceneModelSO 로드", LoadSceneModelStepAsync));
            return stepQueue;
        }

        private async Awaitable LoadSceneModelStepAsync()
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
        }

        private void ResetLoadProgress(int _totalSteps)
        {
            loadTotalSteps = Mathf.Max(1, _totalSteps);
            loadCurrentStep = 0;
            progressBarView?.SetProgress(0f);
        }

        /// <summary>
        /// 준비 작업 스텝이 하나 완료될 때마다 호출되어 ProgressBar를 갱신한다.
        /// </summary>
        private void CompleteLoadStep()
        {
            loadCurrentStep++;
            float progress = Mathf.Clamp01((float)loadCurrentStep / loadTotalSteps);
            progressBarView?.SetProgress(progress);
        }

        /// <summary>
        /// 준비 작업 진행률이 100%가 된 뒤 호출된다.
        /// 대상 Scene 구성을 조회하여 SceneLoadController에 순수 Scene 전환만 위임한다.
        /// </summary>
        private async Awaitable TransitionToTargetSceneAsync()
        {
            SceneModel targetModel = SceneLoadController.Instance.GetSceneModel(targetSceneTag);
            if (targetModel == null)
            {
                Utils.CreateLogMessage<DummySceneController>($"{targetSceneTag}는 존재하지 않습니다.");
                return;
            }

            string bootstrapSceneName = SceneManager.GetActiveScene().name;
            await SceneLoadController.Instance.TransitionScenesAsync(targetModel.loadScenes, targetModel.activeScene, bootstrapSceneName);
        }
        #endregion
    }
}
