using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class LoginSceneManager : MonoBehaviour
    {
        #region Variable
        [Header("Init UI Variable")]
        [SerializeField] private GameObject initImageObject;

        private UI_LoginSceneView loginSceneView;
        #endregion

        #region LifeCycle
        private void Start()
        {
            // LoginScene으로 전환이 완료된 시점이므로, 부트스트랩 단계에서 켜져 있던 로딩바를 풀로 반환한다.
            GameManager.Instance?.HideLoadingBar();
            CreateLoginUI();
        }

        private void OnDestroy()
        {
            if (loginSceneView != null)
            {
                loginSceneView.LoginSucceeded -= OnLoginSucceeded;
            }
        }
        #endregion

        #region Method
        private void CreateLoginUI()
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LoginSceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.UI_LoginScene.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);

                if (instance != null && instance.TryGetComponent(out loginSceneView))
                {
                    loginSceneView.LoginSucceeded += OnLoginSucceeded;
                }
            });
        }

        private void OnLoginSucceeded()
        {
            // 로그인 성공 후 LobbyScene으로 전환되는 동안 다시 보여줄 로딩바를 대여한다.
            GameManager.Instance?.ShowLoadingBar();

            // 로그인 직후 서버의 캐릭터 세이브 데이터를 한 번 동기화해 로컬 캐시(HasSaveData/Load)를 최신 상태로 맞춘다.
            // 신규 유저(캐릭터 생성 전)는 서버가 없음을 응답할 수 있으며 이는 실패로 취급되지만, 씬 전환은 그대로 진행한다.
            SaveDataManager.Instance?.FetchFromServerAsync(_ => TransitionToLobbyScene());
        }

        private void TransitionToLobbyScene()
        {
            if (SceneLoadManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LoginSceneManager>("SceneLoadManager.Instance가 null입니다.");
                return;
            }

            SceneLoadManager.Instance.LoadSceneByTags("LobbyScene");
        }
        #endregion
    }
}
