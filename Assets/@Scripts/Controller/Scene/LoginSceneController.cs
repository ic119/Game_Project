using Incheol.Module;
using Incheol.Util;
using System;
using UnityEngine;

namespace Incheol.Scene.Login
{
    public class LoginSceneController : MonoBehaviour
    {
        #region Variable
        private GameObject loginSceneInstance;
        #endregion

        #region LifeCycle
        private async void Start()
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void OnDestroy()
        {
            AddressableAssetController addressableController = AddressableAssetController.Instance;
            if (addressableController == null)
            {
                return;
            }

            if (loginSceneInstance != null)
            {
                addressableController.ReleaseInstance(loginSceneInstance);
                loginSceneInstance = null;
            }

            addressableController.Release(AddressKey.UI_LoginScene);
            addressableController.Release(AddressKey.UI_AlarmPopup);
        }
        #endregion

        #region Method
        private async Awaitable InitializeAsync()
        {
            AddressableAssetController addressableController = AddressableAssetController.Instance;
            if (addressableController == null)
            {
                //Utils.CreateLogMessage<LoginSceneController>("AddressableController를 찾을 수 없습니다.");
                return;
            }

            await PreloadAddressableKeysAsync(addressableController);

            loginSceneInstance = addressableController.Instantiate(AddressKey.UI_LoginScene, gameObject.transform);
        }

        /// <summary>
        /// 로그인 씬 진입 시 필요한 Addressable Asset들을 미리 Load한다.
        /// </summary>
        private async Awaitable PreloadAddressableKeysAsync(AddressableAssetController _addressableController)
        {
            await _addressableController.LoadAsync<GameObject>(AddressKey.UI_LoginScene);
            await _addressableController.LoadAsync<GameObject>(AddressKey.UI_AlarmPopup);
            await _addressableController.LoadAsync<GameObject>(AddressKey.BeginnerVillage);
            await _addressableController.LoadAsync<GameObject>(AddressKey.PlayerPrefab);
            await _addressableController.LoadAsync<GameObject>(AddressKey.UI_MainScene);
            await _addressableController.LoadAsync<GameObject>(AddressKey.UI_InventoryViewPopup);
            await _addressableController.LoadAsync<GameObject>(AddressKey.UI_CharacterInfoVIewPopup);
        }
        #endregion
    }
}
