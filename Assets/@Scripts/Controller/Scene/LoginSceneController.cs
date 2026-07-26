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
            AddressableController addressableController = AddressableController.Instance;
            if (addressableController == null)
            {
                return;
            }

            if (loginSceneInstance != null)
            {
                addressableController.ReleaseInstance(loginSceneInstance);
                loginSceneInstance = null;
            }

            addressableController.ReleaseHandler(AddressKey.UI_LoginScene.ToString());
            addressableController.ReleaseHandler(AddressKey.UI_AlarmPopup.ToString());
        }
        #endregion

        #region Method
        private async Awaitable InitializeAsync()
        {
            AddressableController addressableController = AddressableController.Instance;
            if (addressableController == null)
            {
                Utils.CreateLogMessage<LoginSceneController>("AddressableController를 찾을 수 없습니다.");
                return;
            }

            RegisterAddressableKeys(addressableController);
            addressableController.LoadPrefabAddressFromHashSet();

            await addressableController.InstantiateAsset(
                AddressKey.UI_LoginScene.ToString(),
                gameObject,
                OnLoginSceneInstantiated);
        }

        private void RegisterAddressableKeys(AddressableController _addressableController)
        {
            _addressableController.AddKeyHashSet(AddressKey.UI_LoginScene.ToString());
            _addressableController.AddKeyHashSet(AddressKey.UI_AlarmPopup.ToString());
            _addressableController.AddKeyHashSet(AddressKey.BeginnerVillage.ToString());
            _addressableController.AddKeyHashSet(AddressKey.PlayerPrefab.ToString());
            _addressableController.AddKeyHashSet(AddressKey.UI_MainScene.ToString());
            _addressableController.AddKeyHashSet(AddressKey.UI_InventoryViewPopup.ToString());
            _addressableController.AddKeyHashSet(AddressKey.UI_CharacterInfoVIewPopup.ToString());
        }

        private void OnLoginSceneInstantiated(GameObject _instance)
        {
            if (this == null)
            {
                AddressableController.Instance?.ReleaseInstance(_instance);
                return;
            }

            loginSceneInstance = _instance;
        }
        #endregion
    }
}
