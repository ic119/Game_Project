using Incheol.Modules;
using Incheol.Utils;
using Incheol.View.UI;
using System.Threading;
using UnityEngine;

namespace Incheol.Presenter
{
    public class BootstrapSceneManager : MonoBehaviour
    {
        private UI_LoadingBarView loadingBarView;

        private ISequenceStep[] bootstrapSteps;

        private class LoadSceneManage : ISequenceStep
        {
            public string StepName => "씬 로드 매니저 초기화";

            public async Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                if (SceneLoadManager.Instance == null)
                {
                    DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("SceneLoadManager.Instance가 null입니다.");
                    return false;
                }

                bool isDone = false;
                bool isSuccess = false;

                SceneLoadManager.Instance.Init(success =>
                {
                    isSuccess = success;
                    isDone = true;
                });

                while (!isDone)
                {
                    await Awaitable.NextFrameAsync(_cancellationToken);
                }

                if (!isSuccess)
                {
                    DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("SceneLoadManager Init 실패로 Bootstrap 시퀀스를 중단합니다.");
                }

                return isSuccess;
            }
        }

        private class AddressableAssetManage : ISequenceStep
        {
            public string StepName => "어드레서블 에셋 초기화";

            public async Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                if (AddressableAssetManager.Instance != null)
                {
                    AddressableAssetManager.Instance.Init();
                }

                await Awaitable.NextFrameAsync(_cancellationToken);

                return true;
            }
        }

        private class SoundManage : ISequenceStep
        {
            public string StepName => "사운드 매니저 초기화";

            public async Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                // SoundController.Instance에 접근하는 것만으로 Awake()가 호출되어
                // Inspector에 등록된 SoundClip 목록으로 AudioSource가 준비된다.
                _ = SoundManager.Instance;

                await Awaitable.NextFrameAsync(_cancellationToken);

                return true;
            }
        }

        private class ChangeSceneManage : ISequenceStep
        {
            public string StepName => "메인 씬으로 전환 준비";

            public async Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                await Awaitable.NextFrameAsync(_cancellationToken);

                if (SceneLoadManager.Instance == null || !SceneLoadManager.Instance.IsInitialized)
                {
                    DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("LoadSceneController가 초기화되지 않아 main 씬으로 전환할 수 없습니다.");
                    return false;
                }

                SceneLoadManager.Instance.LoadSceneByTags("LoginScene");

                return true;
            }
        }

        private class ServerConnectManage : ISequenceStep
        {
            public string StepName => "서버 연결";

            public async Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                if (ServerConnectManager.Instance == null)
                {
                    DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("ServerConnectManager.Instance가 null입니다.");
                    return false;
                }

                await Awaitable.NextFrameAsync(_cancellationToken);

                return true;
            }
        }

        private class GameManage : ISequenceStep
        {
            private readonly BootstrapSceneManager owner;

            public GameManage(BootstrapSceneManager _owner)
            {
                owner = _owner;
            }

            public string StepName => "GameManager 생성";

public async Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                if (GameManager.Instance == null)
                {
                    DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("GameManager.Instance가 null입니다.");
                    return false;
                }

                bool isDone = false;
                bool isSuccess = false;

                GameManager.Instance.Init(success =>
                {
                    isSuccess = success;
                    isDone = true;
                });

                while (!isDone)
                {
                    await Awaitable.NextFrameAsync(_cancellationToken);
                }

                if (!isSuccess)
                {
                    DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("GameManager Init 실패로 Bootstrap 시퀀스를 중단합니다.");
                    return false;
                }

                owner.loadingBarView = GameManager.Instance.LoadingBarView;
                owner.loadingBarView?.UpdateProgress(0f);

                return true;
            }
        }

        #region LifeCycle
private void Start()
        {
            GameManage gameManage = new GameManage(this);
            LoadSceneManage loadSceneManage = new LoadSceneManage();
            AddressableAssetManage addressableAssetManage = new AddressableAssetManage();
            SoundManage soundManage = new SoundManage();
            ServerConnectManage serverConnectManage = new ServerConnectManage();
            ChangeSceneManage changeSceneManage = new ChangeSceneManage();

            bootstrapSteps = new ISequenceStep[]
            {
                gameManage,
                loadSceneManage,
                addressableAssetManage,
                soundManage,
                serverConnectManage,
                changeSceneManage,
            };

            SequenceManager.Instance.Enqueue(gameManage);
            SequenceManager.Instance.Enqueue(loadSceneManage);
            SequenceManager.Instance.Enqueue(addressableAssetManage);
            SequenceManager.Instance.Enqueue(soundManage);
            SequenceManager.Instance.Enqueue(serverConnectManage);
            SequenceManager.Instance.Enqueue(changeSceneManage);

            SequenceManager.Instance.OnStepCompleted += OnBootstrapStepCompleted;
            SequenceManager.Instance.DoSequenceAction(OnBootstrapSequenceCompleted);
        }

        /// <summary>
        /// SequenceManager가 등록된 모든 부트스트랩 단계를 마쳤을 때 호출된다.
        /// 실패 시(예: LoadSceneManage/AddressableAssetManage 단계 실패) 지금까지는 아무에게도 통지되지 않아
        /// 사용자 입장에서 부트스트랩 화면이 이유 없이 멈춘 것처럼 보였다 — 최소한 콘솔에는 명확히 남긴다.
        /// </summary>
        private void OnBootstrapSequenceCompleted(bool _isSuccess)
        {
            if (!_isSuccess)
            {
                DebugLogManager.GenerateErrorMessage<BootstrapSceneManager>("부트스트랩 시퀀스가 실패로 종료되었습니다. 게임을 재시작해야 할 수 있습니다.");
            }
        }

        /// <summary>
        /// SequenceManager의 단계가 하나 끝날 때마다 호출된다. sequenceQueue에 등록된 전체 단계 수로 100을 나눈 만큼씩
        /// 진행률이 누적 증가하며, 이 값을 UI_BootstrapSceneView(progressBarView의 퍼센트 텍스트/게이지)에 실시간으로 반영한다.
        /// 또한 방금 완료된 단계의 StepName을 progressBarView의 타이틀 텍스트로 표출한다.
        /// </summary>
        private void OnBootstrapStepCompleted(int _completedCount, int _totalCount)
        {
            if (loadingBarView == null || _totalCount <= 0)
            {
                return;
            }

            float progress = (float)_completedCount / _totalCount;
            loadingBarView.UpdateProgress(progress);

            int stepIndex = _completedCount - 1;
            if (bootstrapSteps != null && stepIndex >= 0 && stepIndex < bootstrapSteps.Length)
            {
                loadingBarView.UpdateTitle($"{bootstrapSteps[stepIndex].StepName} 완료");
            }
        }

        private void OnDestroy()
        {
            if (SequenceManager.Instance != null)
            {
                SequenceManager.Instance.OnStepCompleted -= OnBootstrapStepCompleted;
            }
        }
        #endregion
    }
}
