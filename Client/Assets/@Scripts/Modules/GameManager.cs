using Incheol.Utils;

using Incheol.Models.Define;
using Incheol.Models.SO;

using Incheol.View.UI;
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine;

namespace Incheol.Modules
{
    public class GameManager : SingletonObject<GameManager>
    {

private const string addressableAssetScriptableObjectName = "AddressableAssetModelSO";
        private const string bootstrapSceneTag = "BootstrapScene";

        /// <summary>
        /// AddressableAssetModelSO에서 tags가 "BootstrapScene"인 항목의 preloadAddressableKeys를 로드하고 생성한다.
        /// </summary>
        public void Init(Action<bool> _onComplete = null)
        {
            _ = InitAsync(_onComplete);
        }

        private async Awaitable InitAsync(Action<bool> _onComplete)
        {
            List<AddressableAssetKey> bootstrapKeys = await LoadBootstrapAddressableKeysAsync();

            if (bootstrapKeys == null)
            {
                _onComplete?.Invoke(false);
                return;
            }

            bool isSuccess = await InstantiateAddressableKeysAsync(bootstrapKeys);
            _onComplete?.Invoke(isSuccess);
        }

        private async Awaitable<List<AddressableAssetKey>> LoadBootstrapAddressableKeysAsync()
        {
            AsyncOperationHandle<AddressableAssetModelSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<AddressableAssetModelSO>(addressableAssetScriptableObjectName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>($"AddressableAssetModelSO 로드 실패(잘못된 Key) Key : {addressableAssetScriptableObjectName}, Exception : {exception}");
                return null;
            }

            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync();
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>($"AddressableAssetModelSO 로드 실패(Addressables Status : {handle.Status}) Key : {addressableAssetScriptableObjectName}");
                Addressables.Release(handle);
                return null;
            }

            List<AddressableAssetModel> models = handle.Result.addressableAssetModels;
            AddressableAssetModel bootstrapModel = models?.Find(model => model != null && model.tags == bootstrapSceneTag);

            Addressables.Release(handle);

            if (bootstrapModel == null)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>($"AddressableAssetModelSO에 tag '{bootstrapSceneTag}'에 대한 설정이 없습니다.");
                return null;
            }

            return bootstrapModel.preloadAddressableKeys ?? new List<AddressableAssetKey>();
        }

private async Awaitable<bool> InstantiateAddressableKeysAsync(List<AddressableAssetKey> _keys)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>("AddressableAssetManager.Instance가 null입니다.");
                return false;
            }

            bool isSuccess = true;

            for (int i = 0; i < _keys.Count; i++)
            {
                AddressableAssetKey key = _keys[i];
                if (key == AddressableAssetKey.None)
                {
                    continue;
                }

                string keyString = key.ToString();

                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(keyString);
                await AddressableAssetManager.Instance.WaitForLoadAsync(keyString);

                if (AddressableAssetManager.Instance.GetHandler(keyString, out AsyncOperationHandle loadedHandle) &&
                    loadedHandle.Result is GameObject prefab)
                {
                    GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab);

                    if (instance != null && instance.TryGetComponent(out UI_LoadingBarView loadingBarView))
                    {
                        LoadingBarView = loadingBarView;
                    }
                }
                else
                {
                    isSuccess = false;
                    DebugLogManager.GenerateErrorMessage<GameManager>($"Addressable 생성 실패 Key : {keyString}");
                }
            }

            return isSuccess;
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    

public UI_LoadingBarView LoadingBarView { get; private set; }
}
}
