using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class LobbySceneManager : MonoBehaviour
    {
        #region Variable
        private const string lobbySceneTag = "LobbyScene";
        #endregion

        #region LifeCycle
        private void Start()
        {
            if (GameManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("GameManager.Instance가 null입니다.");
                return;
            }

            // 로딩바 표시 → LobbyScene 태그의 Addressable 프리로드/생성(진행률 표출) → 완료 시 로딩바 숨김까지
            // GameManager.EnterSceneWithLoadingBar 한 번으로 처리된다.
            GameManager.Instance.EnterSceneWithLoadingBar(lobbySceneTag, OnLobbyReady);
        }
        #endregion

        #region Method
        private void OnLobbyReady(bool _isSuccess)
        {
            if (!_isSuccess)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("LobbyScene Addressable 로드/생성 중 일부가 실패했습니다.");
            }
        }
        #endregion
    }
}
