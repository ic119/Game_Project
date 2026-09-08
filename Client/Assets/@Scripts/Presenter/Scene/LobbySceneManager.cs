using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class LobbySceneManager : MonoBehaviour
    {
        #region Variable
        private const string lobbySceneTag = "LobbyScene";

        private UI_LobbySceneView lobbySceneView;
        private UI_CharacterCreatePopup characterCreatePopup;
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

        private void OnDestroy()
        {
            if (characterCreatePopup != null)
            {
                characterCreatePopup.OnCreateRequested = null;
            }
        }
        #endregion

        #region Method
        private void OnLobbyReady(bool _isSuccess)
        {
            if (!_isSuccess)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("LobbyScene Addressable 로드/생성 중 일부가 실패했습니다.");
            }

            CreateLobbyUI();
        }

        /// <summary>
        /// LobbyScene의 메인 UI(UI_LobbyScene)를 Addressable로 로드/인스턴스화한다.
        /// LoginSceneManager.CreateLoginUI와 동일한 패턴 - 씬의 메인 UI는 범용 프리로드 태그가 아니라
        /// 각 씬 Presenter가 직접 로드/생성한다.
        /// </summary>
        private void CreateLobbyUI()
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.UI_LobbyScene.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<LobbySceneManager>($"UI_LobbyScene 로드 실패 Key : {AddressableAssetKey.UI_LobbyScene}");
                    return;
                }

                GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                instance.TryGetComponent(out lobbySceneView);

                // CharacterCreateContainer(팝업)는 기본 비활성 상태이므로 GetComponentInChildren에 includeInactive를 반드시 켜야 한다.
                characterCreatePopup = instance.GetComponentInChildren<UI_CharacterCreatePopup>(true);
                WireCharacterCreatePopup();
            });
        }

        /// <summary>
        /// 캐릭터 생성 팝업의 저장 처리기(OnCreateRequested)를 SaveDataManager로 연결한다.
        /// UI_CharacterCreatePopup은 Func&lt;UserSaveData, bool&gt;의 동기 반환을 요구하므로,
        /// SaveDataManager.CreateNew(로컬 캐시 즉시 반영 + 서버 저장은 백그라운드)를 그대로 위임한다.
        /// </summary>
        private void WireCharacterCreatePopup()
        {
            if (characterCreatePopup == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("UI_CharacterCreatePopup을 찾을 수 없어 생성 요청을 연결하지 못했습니다.");
                return;
            }

            characterCreatePopup.OnCreateRequested = OnCharacterCreateRequested;
            DebugLogManager.GenerateLogMessage<LobbySceneManager>($"UI_CharacterCreatePopup 연결 완료. InstanceID={characterCreatePopup.GetInstanceID()}");
        }

        private bool OnCharacterCreateRequested(UserSaveData _saveData)
        {
            if (SaveDataManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("SaveDataManager.Instance가 null입니다.");
                return false;
            }

            return SaveDataManager.Instance.CreateNew(_saveData);
        }
        #endregion
    }
}
