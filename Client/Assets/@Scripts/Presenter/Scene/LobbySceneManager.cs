using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using Incheol.View.UI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Presenter.Scene
{
    public class LobbySceneManager : MonoBehaviour
    {
        #region Variable
        private const string lobbySceneTag = "LobbyScene";
        private const string gameSceneTag = "GameScene";

        private UI_LobbySceneView lobbySceneView;
        private UI_CharacterCreatePopup characterCreatePopup;
        #endregion

        #region LifeCycle
        private void Start()
        {
            LoadAndInstantiateLobbySceneAssets();
        }

        private void OnDestroy()
        {
            if (characterCreatePopup != null)
            {
                characterCreatePopup.OnCreateRequested = null;
            }

            if (lobbySceneView != null)
            {
                lobbySceneView.OnStartRequested -= OnStartRequested;
                lobbySceneView.OnDeleteRequested -= OnDeleteRequested;
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// AddressableAssetModelSO에서 tags가 "LobbyScene"인 항목(UI_LobbyScene)을 로드해 생성한다.
        /// GameSceneManager.LoadAndInstantiateGameSceneAssets와 동일한 패턴 - 로딩바는 실제 UI_LobbyScene이
        /// 인스턴스화될 때까지 대기한 뒤에만 숨긴다. 예전에는 LobbyScene의 프리로드 키 목록이 비어있어
        /// EnterSceneWithLoadingBar가 UI 생성 전에 로딩바부터 숨겨버려, 화면에 UI_LobbyScene이 잠깐
        /// 깜빡이는 문제가 있었다.
        /// </summary>
        private async void LoadAndInstantiateLobbySceneAssets()
        {
            if (GameManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("GameManager.Instance가 null입니다.");
                return;
            }

            GameManager.Instance.ShowLoadingBar();

            List<AddressableAssetKey> keys = await GameManager.Instance.LoadAddressableKeysByTagAsync(lobbySceneTag);

            if (keys == null || this == null)
            {
                GameManager.Instance?.HideLoadingBar();
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("AddressableAssetManager.Instance가 null입니다.");
                GameManager.Instance.HideLoadingBar();
                return;
            }

            foreach (AddressableAssetKey key in keys)
            {
                if (key == AddressableAssetKey.None)
                {
                    continue;
                }

                string keyString = key.ToString();

                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(keyString);
                await AddressableAssetManager.Instance.WaitForLoadAsync(keyString);

                if (this == null)
                {
                    return;
                }

                if (!AddressableAssetManager.Instance.GetHandler(keyString, out AsyncOperationHandle handle) ||
                    handle.Result is not GameObject prefab)
                {
                    DebugLogManager.GenerateErrorMessage<LobbySceneManager>($"LobbyScene Addressable 로드 실패 Key : {keyString}");
                    continue;
                }

                GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);

                if (key == AddressableAssetKey.UI_LobbyScene)
                {
                    SetupLobbyUI(instance);
                }
            }

            GameManager.Instance?.HideLoadingBar();
        }

        /// <summary>
        /// 생성된 UI_LobbyScene 인스턴스에서 뷰/팝업 참조를 얻고 이벤트를 연결한다.
        /// </summary>
        private void SetupLobbyUI(GameObject _instance)
        {
            _instance.TryGetComponent(out lobbySceneView);

            if (lobbySceneView != null)
            {
                lobbySceneView.OnStartRequested += OnStartRequested;
                lobbySceneView.OnDeleteRequested += OnDeleteRequested;
            }

            // CharacterCreateContainer(팝업)는 기본 비활성 상태이므로 GetComponentInChildren에 includeInactive를 반드시 켜야 한다.
            characterCreatePopup = _instance.GetComponentInChildren<UI_CharacterCreatePopup>(true);
            WireCharacterCreatePopup();
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

        /// <summary>
        /// UI_LobbySceneView의 시작(이어하기) 버튼 클릭 시 호출된다. 저장된 캐릭터가 있을 때만 버튼이 보이므로
        /// 여기서는 별도 검증 없이 바로 GameScene으로 전환한다.
        /// </summary>
        private void OnStartRequested()
        {
            if (SceneLoadManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("SceneLoadManager.Instance가 null입니다.");
                return;
            }

            SceneLoadManager.Instance.LoadSceneByTags(gameSceneTag);
        }

        /// <summary>
        /// UI_LobbySceneView의 삭제 버튼 클릭 시 호출된다. 서버 삭제가 확인된 뒤에만 로컬 캐시가 비워지므로,
        /// 완료 콜백에서 성공 여부와 무관하게 최신 상태로 뷰를 다시 그린다.
        /// </summary>
        private void OnDeleteRequested()
        {
            if (SaveDataManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LobbySceneManager>("SaveDataManager.Instance가 null입니다.");
                return;
            }

            // DeleteAsync가 성공하면 SelectedCharacterId가 곳바로 해제되므로, 삭제 대상 id를 미리 보관해둔다.
            long? deletedCharacterId = SaveDataManager.Instance.SelectedCharacterId;

            SaveDataManager.Instance.DeleteAsync(isSuccess =>
            {
                if (isSuccess && deletedCharacterId.HasValue)
                {
                    // 전체 목록을 서버에서 다시 받아오는 대신, 삭제된 캐릭터에 해당하는
                    // UI_CharacterListItem만 contentRect에서 직접 제거한다.
                    lobbySceneView?.RemoveCharacterListItem(deletedCharacterId.Value);
                }

                lobbySceneView?.RefreshState();
            });
        }
        #endregion
    }
}
