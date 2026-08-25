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
        /// <summary>
        /// LoadingBarView 참조는 LoginScene 이후에도 유지되어야 하므로 GameManager 자체도 씬 전환에 파괴되지 않아야 한다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

        private const string addressableAssetScriptableObjectName = "AddressableAssetModelSO";
        private const string bootstrapSceneTag = "BootstrapScene";

        /// <summary>
        /// AddressableAssetModelSO에서 tags가 "BootstrapScene"인 항목의 preloadAddressableKeys를 로드하고 생성한다.
        /// </summary>
        public void Init(Action<bool> _onComplete = null)
        {
            _ = InitAsync(bootstrapSceneTag, null, _onComplete);
        }

        /// <summary>
        /// AddressableAssetModelSO에서 tags가 _tag인 항목의 preloadAddressableKeys를 로드하고 ObjectPoolManager를 통해 생성한다.
        /// 키 하나가 끝날 때마다(성공/실패 무관) (완료 수, 이번 호출에 등록된 전체 키 수)를 _onProgress로 알려준다.
        /// 로딩바 진행률("3/6 완료") 표시 등에 사용할 수 있다.
        /// </summary>
        public void LoadAndInstantiateByTag(string _tag, Action<int, int> _onProgress = null, Action<bool> _onComplete = null)
        {
            _ = InitAsync(_tag, _onProgress, _onComplete);
        }

        private async Awaitable InitAsync(string _tag, Action<int, int> _onProgress, Action<bool> _onComplete)
        {
            List<AddressableAssetKey> keys = await LoadAddressableKeysByTagAsync(_tag);

            if (keys == null)
            {
                _onComplete?.Invoke(false);
                return;
            }

            bool isSuccess = await InstantiateAddressableKeysAsync(keys, _onProgress);
            _onComplete?.Invoke(isSuccess);
        }

        private async Awaitable<List<AddressableAssetKey>> LoadAddressableKeysByTagAsync(string _tag)
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
            AddressableAssetModel targetModel = models?.Find(model => model != null && model.tags == _tag);

            Addressables.Release(handle);

            if (targetModel == null)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>($"AddressableAssetModelSO에 tag '{_tag}'에 대한 설정이 없습니다.");
                return null;
            }

            return targetModel.preloadAddressableKeys ?? new List<AddressableAssetKey>();
        }

        private async Awaitable<bool> InstantiateAddressableKeysAsync(List<AddressableAssetKey> _keys, Action<int, int> _onProgress = null)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>("AddressableAssetManager.Instance가 null입니다.");
                return false;
            }

            if (ObjectPoolManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameManager>("ObjectPoolManager.Instance가 null입니다.");
                return false;
            }

            bool isSuccess = true;
            int totalCount = _keys.Count;

            for (int i = 0; i < _keys.Count; i++)
            {
                AddressableAssetKey key = _keys[i];
                if (key == AddressableAssetKey.None)
                {
                    _onProgress?.Invoke(i + 1, totalCount);
                    continue;
                }

                string keyString = key.ToString();

                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(keyString);
                await AddressableAssetManager.Instance.WaitForLoadAsync(keyString);

                if (AddressableAssetManager.Instance.GetHandler(keyString, out AsyncOperationHandle loadedHandle) &&
                    loadedHandle.Result is GameObject)
                {
                    // ObjectPoolManager를 통해 생성하면 ObjectPoolManager(PersistAcrossScenes)의 자식으로 붙어
                    // 씬 전환에도 파괴되지 않고, 이후 다른 씬에서 같은 Key로 Get()하면 재사용된다.
                    GameObject instance = ObjectPoolManager.Instance.Get(keyString);

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

                _onProgress?.Invoke(i + 1, totalCount);
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
