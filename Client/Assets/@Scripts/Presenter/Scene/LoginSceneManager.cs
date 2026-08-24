using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class LoginSceneManager : MonoBehaviour
    {
        #region Variable
        [Header("Init UI Variable")]
        [SerializeField] private GameObject initImageObject;
        #endregion

        #region LifeCycle
        private void Start()
        {
            CreateLoginUI();
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

                AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
            });
        }
        #endregion
    }
}
