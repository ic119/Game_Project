using Incheol.Modules;
using Incheol.Utils;
using System.Threading;
using UnityEngine;

namespace Incheol.Presenter
{
    public class BootstrapSceneManager : MonoBehaviour
    {
        private class LoadSceneManage : ISequenceStep
        {
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
            public Awaitable<bool> Execute(CancellationToken _cancellationToken)
            {
                throw new System.NotImplementedException();
            }
        }

        #region LifeCycle
        private void Start()
        {
            LoadSceneManage loadSceneManage = new LoadSceneManage();
            AddressableAssetManage addressableAssetManage = new AddressableAssetManage();
            SoundManage soundManage = new SoundManage();
            ChangeSceneManage changeSceneManage = new ChangeSceneManage();
            ServerConnectManage serverConnectManage = new ServerConnectManage();

            SequenceManager.Instance.Enqueue(loadSceneManage);
            SequenceManager.Instance.Enqueue(addressableAssetManage);
            SequenceManager.Instance.Enqueue(soundManage);
            SequenceManager.Instance.Enqueue(changeSceneManage);
            SequenceManager.Instance.Enqueue(serverConnectManage);

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
        #endregion
    }
}
