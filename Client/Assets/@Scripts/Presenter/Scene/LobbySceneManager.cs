using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class LobbySceneManager : MonoBehaviour
    {
        #region Variable
        private const string lobbySceneTag = "LobbyScene";

        private bool hasReleasedLoadingBar;
        #endregion

        #region LifeCycle
        private void Start()
        {
            LoadLobbyAssets();
        }
        #endregion

        #region Method
        /// <summary>
        /// AddressableAssetModelSO에서 tags가 "LobbyScene"인 항목의 preloadAddressableKeys를 로드/생성하면서,
        /// 로그인 성공 시 다시 대여해 둔 UI_LoadingBarView에 진행률을 표출한다.
        /// </summary>
        private void LoadLobbyAssets()
        {
            hasReleasedLoadingBar = false;

            if (GameManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("GameManager.Instance가 null입니다.");
                return;
            }

            GameManager.Instance.LoadAndInstantiateByTag(lobbySceneTag, OnLoadProgress, OnLoadComplete);
        }

        private void OnLoadProgress(int _completedCount, int _totalCount)
        {
            if (_totalCount <= 0)
            {
                return;
            }

            float progress = (float)_completedCount / _totalCount;

            if (GameManager.Instance != null && GameManager.Instance.LoadingBarView != null)
            {
                GameManager.Instance.LoadingBarView.UpdateProgress(progress);
            }

            if (progress >= 1f)
            {
                ReleaseLoadingBarView();
            }
        }

        private void OnLoadComplete(bool _isSuccess)
        {
            if (!_isSuccess)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("LobbyScene Addressable 로드/생성 중 일부가 실패했습니다.");
            }

            // preloadAddressableKeys가 비어 있으면 OnLoadProgress가 한 번도 호출되지 않으므로 여기서 보강 처리한다.
            ReleaseLoadingBarView();
        }

        /// <summary>
        /// 로딩이 끝난 뒤(성공/실패, 진행률 100% 무관하게 최종적으로) LoadingBarView를 풀로 반환한다.
        /// 진행률 콜백과 완료 콜백에서 중복 호출될 수 있으므로 한 번만 실행되도록 가드한다.
        /// </summary>
        private void ReleaseLoadingBarView()
        {
            if (hasReleasedLoadingBar)
            {
                return;
            }

            if (GameManager.Instance == null || GameManager.Instance.LoadingBarView == null)
            {
                return;
            }

            if (ObjectPoolManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("ObjectPoolManager.Instance가 null입니다.");
                return;
            }

            hasReleasedLoadingBar = true;
            ObjectPoolManager.Instance.Release(GameManager.Instance.LoadingBarView.gameObject);
        }
        #endregion
    }
}
