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
