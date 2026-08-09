using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using Incheol.Models.SO;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace Incheol.Modules
{
    public class SceneLoadManager : SingletonObject<SceneLoadManager>
    {
        /// <summary>
        /// 씬 전환 중 자기 자신이 속한 씬이 언로드되어도 매니저가 파괴되지 않도록 유지한다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

        #region Nested
        /// <summary>
        /// SceneLoadAsync에서 순차 처리할 단일 로드 업무
        /// </summary>
        private interface ILoadTask
        {
            Awaitable ExecuteAsync();
        }

        private class AddressableLoadTask : ILoadTask
        {
            private readonly string key;
            private const float loadTimeoutSeconds = 30.0f;

            public AddressableLoadTask(string _key)
            {
                key = _key;
            }

            public async Awaitable ExecuteAsync()
            {
                if (AddressableAssetManager.Instance == null || string.IsNullOrEmpty(key))
                {
                    return;
                }

                AddressableAssetManager controller = AddressableAssetManager.Instance;
                controller.AddKeyHashSet(key);
                controller.LoadPrefabAddress<GameObject>(key);

                // 이미 캐시되어 있으면 즉시 완료
                if (controller.IsLoaded(key))
                {
                    return;
                }

                // 핸들 폴링 + NextFrameAsync 루프는 AddressableAssetController.WaitForLoadAsync로 공통화되어 있으므로
                // 여기서는 타임아웃 조건만 전달해 중복 구현을 피한다.
                float startTime = Time.unscaledTime;
                await controller.WaitForLoadAsync(key, () => Time.unscaledTime - startTime >= loadTimeoutSeconds);

                if (controller.IsLoaded(key))
                {
                    return;
                }

                if (controller.HasLoadFailed(key))
                {
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"Addressable 프리로드 실패 Key : {key}");
                    return;
                }

                // 위 두 경우가 아니라면 타임아웃으로 대기가 중단된 것이다.
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"Addressable 프리로드 타임아웃 Key : {key}, Timeout : {loadTimeoutSeconds}s");
            }
        }

        private class AdditiveSceneLoadTask : ILoadTask
        {
            private readonly string sceneName;
            private readonly string activeSceneName;

            public AdditiveSceneLoadTask(string _sceneName, string _activeSceneName)
            {
                sceneName = _sceneName;
                activeSceneName = _activeSceneName;
            }

public async Awaitable ExecuteAsync()
            {
                if (string.IsNullOrEmpty(sceneName))
                {
                    return;
                }

                // diff 전환 과정에서 이전 씬과 공유되는 씬은 이미 로드되어 있을 수 있으므로,
                // 중복 로드하지 않고 활성 씬 처리만 한다.
                Scene existingScene = SceneManager.GetSceneByName(sceneName);
                if (existingScene.IsValid() && existingScene.isLoaded)
                {
                    if (activeSceneName == sceneName)
                    {
                        SceneManager.SetActiveScene(existingScene);
                    }
                    return;
                }

                AsyncOperation async = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

                if (async == null)
                {
                    // 빌드 세팅에 없는 씬 이름 등으로 로드 자체가 시작되지 못한 경우.
                    // null 상태로 async.isDone에 접근하면 NullReferenceException이 발생하므로 여기서 방어한다.
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"Additive Scene 로드 실패(빌드 세팅에 없는 씬일 수 있음) SceneName : {sceneName}");
                    return;
                }

                while (!async.isDone)
                {
                    await Awaitable.NextFrameAsync();
                }

                if (activeSceneName == sceneName)
                {
                    Scene targetActiveScene = SceneManager.GetSceneByName(sceneName);
                    if (targetActiveScene.IsValid())
                    {
                        SceneManager.SetActiveScene(targetActiveScene);
                    }
                }
            }
        }
        #endregion

        #region Variable
        [SerializeField] Incheol.Models.SO.SceneDataModel currentSceneDataModel;
        private Dictionary<string, Incheol.Models.SO.SceneDataModel> sceneDataModelDictionary;
        private Dictionary<string, Incheol.Models.SO.AddressableAssetModel> addressableAssetModelDictionary;
        private List<Incheol.Models.SO.SceneDataModel> sceneDataModelList;
        private const string sceneDataScriptableObjectName = "SceneDataModelSO";
        private const string addressableAssetScriptableObjectName = "AddressableAssetModelSO";

        /// <summary>
        /// Init이 성공 완료되었는지 여부
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 0 ~ 100. Queue 업무가 완료될수록 100에 가까워진다.
        /// </summary>
        public float currentLoadProgressValue = 0.0f;

        private readonly Queue<ILoadTask> loadTaskQueue = new Queue<ILoadTask>();
        private int totalLoadTaskCount;
        private int completedLoadTaskCount;
        private string currentSceneTag;

        /// <summary>
        /// SceneLoadAsync가 진행 중인 동안 true. LoadSceneByTags의 중복/재진입 호출을 막는 데 사용한다.
        /// </summary>
        private bool isSceneLoading;
        #endregion

        #region Method
        /// <summary>
        /// SceneDataModelSO / AddressableAssetModelSO를 Addressable로 로드한다.
        /// 완료 시 _onComplete(true/false)를 반드시 호출하여 호출측이 무한 대기하지 않도록 한다.
        /// </summary>
        public void Init(Action<bool> _onComplete = null)
        {
            IsInitialized = false;
            LoadSceneDataModelSO(sceneSuccess =>
            {
                if (!sceneSuccess)
                {
                    _onComplete?.Invoke(false);
                    return;
                }

                LoadAddressableAssetModelSO(addressableSuccess =>
                {
                    if (!addressableSuccess)
                    {
                        IsInitialized = false;
                        _onComplete?.Invoke(false);
                        return;
                    }

                    IsInitialized = true;
                    _onComplete?.Invoke(true);
                });
            });
        }

private void LoadSceneDataModelSO(Action<bool> _onComplete)
        {
            AsyncOperationHandle<Incheol.Models.SO.SceneDataModelSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<Incheol.Models.SO.SceneDataModelSO>(sceneDataScriptableObjectName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"SceneDataModelSO 로드 실패(잘못된 Key) Key : {sceneDataScriptableObjectName}, Exception : {exception}");
                _onComplete?.Invoke(false);
                return;
            }

            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    sceneDataModelDictionary = null;
                    sceneDataModelList = null;

                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"SceneDataModelSO 로드 실패(Addressables Status : {result.Status}) Key : {sceneDataScriptableObjectName}");
                    _onComplete?.Invoke(false);
                    return;
                }

                sceneDataModelDictionary = new Dictionary<string, Incheol.Models.SO.SceneDataModel>();
                sceneDataModelList = result.Result.sceneDataModels;

                if (sceneDataModelList == null || sceneDataModelList.Count == 0)
                {
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>("SceneDataModelSO의 sceneDataModels 리스트가 비어 있습니다.");
                    _onComplete?.Invoke(false);
                    return;
                }

                for (int i = 0; i < sceneDataModelList.Count; i++)
                {
                    Incheol.Models.SO.SceneDataModel model = sceneDataModelList[i];
                    if (model == null || string.IsNullOrEmpty(model.tags))
                    {
                        continue;
                    }

                    if (!sceneDataModelDictionary.ContainsKey(model.tags))
                    {
                        sceneDataModelDictionary.Add(model.tags, model);
                    }
                }

                if (sceneDataModelDictionary.Count == 0)
                {
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>("SceneDataModelSO의 모든 항목이 유효하지 않은 tags를 갖고 있습니다.");
                    _onComplete?.Invoke(false);
                    return;
                }
                _onComplete?.Invoke(true);
            };
        }

private void LoadAddressableAssetModelSO(Action<bool> _onComplete)
        {
            AsyncOperationHandle<Incheol.Models.SO.AddressableAssetModelSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<Incheol.Models.SO.AddressableAssetModelSO>(addressableAssetScriptableObjectName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"AddressableAssetModelSO 로드 실패(잘못된 Key) Key : {addressableAssetScriptableObjectName}, Exception : {exception}");
                _onComplete?.Invoke(false);
                return;
            }

            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    addressableAssetModelDictionary = null;

                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"AddressableAssetModelSO 로드 실패(Addressables Status : {result.Status}) Key : {addressableAssetScriptableObjectName}");
                    _onComplete?.Invoke(false);
                    return;
                }

                addressableAssetModelDictionary = new Dictionary<string, Incheol.Models.SO.AddressableAssetModel>();
                List<Incheol.Models.SO.AddressableAssetModel> models = result.Result.addressableAssetModels;

                if (models == null || models.Count == 0)
                {
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>("AddressableAssetModelSO의 addressableAssetModels 리스트가 비어 있습니다.");
                    _onComplete?.Invoke(false);
                    return;
                }

                for (int i = 0; i < models.Count; i++)
                {
                    Incheol.Models.SO.AddressableAssetModel model = models[i];
                    if (model == null || string.IsNullOrEmpty(model.tags))
                    {
                        continue;
                    }

                    if (!addressableAssetModelDictionary.ContainsKey(model.tags))
                    {
                        addressableAssetModelDictionary.Add(model.tags, model);
                    }
                }

                if (addressableAssetModelDictionary.Count == 0)
                {
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>("AddressableAssetModelSO의 모든 항목이 유효하지 않은 tags를 갖고 있습니다.");
                    _onComplete?.Invoke(false);
                    return;
                }
                _onComplete?.Invoke(true);
            };
        }

public void LoadSceneByTags(string _tagName)
        {
            if (!IsInitialized || sceneDataModelDictionary == null || addressableAssetModelDictionary == null)
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"LoadSceneByTags 호출 전에 Init이 완료되지 않았습니다. tag : {_tagName}");
                return;
            }

            if (string.IsNullOrEmpty(_tagName))
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>("LoadSceneByTags에 빈 tag가 전달되었습니다.");
                return;
            }

            if (!sceneDataModelDictionary.ContainsKey(_tagName))
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"존재하지 않는 Scene tag : {_tagName}. 등록된 tags : [{string.Join(", ", sceneDataModelDictionary.Keys)}]");
                return;
            }

            if (isSceneLoading)
            {
                // 이전 SceneLoadAsync가 아직 끝나지 않은 상태에서 재호출되면 loadTaskQueue/진행률 카운터가
                // 두 요청 사이에서 공유되어 꾰이고, ReleaseAllHandler가 이전 요청이 로딩 중인 핸들을
                // 강제로 해제해버리므로 여기서 막는다.
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"이전 씬 로드가 아직 끝나지 않아 요청을 무시합니다. 요청한 tag : {_tagName}, 진행 중인 tag : {currentSceneTag}");
                return;
            }

            Incheol.Models.SO.SceneDataModel previousSceneDataModel = currentSceneDataModel;

            currentSceneTag = _tagName;
            currentSceneDataModel = sceneDataModelDictionary[_tagName];
            _ = SceneLoadAsync(previousSceneDataModel, currentSceneDataModel);
        }

        /// <summary>
        /// 이전/대상 SceneDataModel을 비교해 필요한 씬만 로드하고, 더 이상 필요없는 씬만 언로드한다.
        /// </summary>
private async Awaitable SceneLoadAsync(Incheol.Models.SO.SceneDataModel _previous, Incheol.Models.SO.SceneDataModel _target)
        {
            isSceneLoading = true;

            try
            {
                await Awaitable.WaitForSecondsAsync(0.2f);

                // 이전 태그 세션에서 로드했던 Addressable 핸들 / 오브젝트 풀 정리 (최초 실행 시 keyDictionary가 비어 있어 no-op)
                AddressableAssetManager.Instance.ReleaseAllHandler();
                //ObjectPoolController.Instance.Init();

                currentLoadProgressValue = 0.0f;
                loadTaskQueue.Clear();
                totalLoadTaskCount = 0;
                completedLoadTaskCount = 0;

                List<string> targetSceneList = _target.loadedSceneList ?? new List<string>();

                // 이전 SceneDataModel이 없으면(최초 전환, 예: BootstrapScene) 현재 활성 씬을
                // 정리 대상으로 간주해 diff에 포함시킨다.
                List<string> previousSceneList = _previous != null
                    ? (_previous.loadedSceneList ?? new List<string>())
                    : new List<string> { SceneManager.GetActiveScene().name };

                List<string> scenesToUnload = previousSceneList.Where(sceneName => !targetSceneList.Contains(sceneName)).ToList();

                EnqueueLoadTasks(_target);
                await ProcessLoadTaskQueueAsync();

                // Queue가 비워져 Progress가 100이 된 뒤에만 씬 전환
                currentLoadProgressValue = 100.0f;

                // 대상 씬에서 더 이상 필요하지 않은(공유되지 않는) 이전 씬만 언로드한다.
                for (int i = 0; i < scenesToUnload.Count; i++)
                {
                    Scene scene = SceneManager.GetSceneByName(scenesToUnload[i]);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        continue;
                    }

                    AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(scene);
                    if (unloadOperation == null)
                    {
                        continue;
                    }

                    while (!unloadOperation.isDone)
                    {
                        await Awaitable.NextFrameAsync();
                    }
                }

                await Awaitable.WaitForSecondsAsync(0.2f);
            }
            finally
            {
                isSceneLoading = false;
            }
        }

        /// <summary>
        /// Addressable / Additive 씬 로드 업무를 Queue에 등록한다.
        /// </summary>
        private void EnqueueLoadTasks(Incheol.Models.SO.SceneDataModel _target)
        {
            List<string> addressableKeys = CollectPreloadKeyStrings(currentSceneTag);
            for (int i = 0; i < addressableKeys.Count; i++)
            {
                loadTaskQueue.Enqueue(new AddressableLoadTask(addressableKeys[i]));
            }

            List<string> sceneTargets = _target.loadedSceneList ?? new List<string>();
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                loadTaskQueue.Enqueue(new AdditiveSceneLoadTask(sceneTargets[i], _target.activeSceneName));
            }

            totalLoadTaskCount = loadTaskQueue.Count;
            completedLoadTaskCount = 0;
            UpdateProgressByQueue();
        }

        /// <summary>
        /// Queue에 쌓인 업무를 모두 동시에 시작한다.
        /// 태스크를 순차로 기다리면 하나가 느려질 때 전체 진행률이 함께 멈추는 문제가 있어,
        /// 각 업무를 병렬로 실행하고 완료된 개수만큼만 진행률을 올린다.
        /// </summary>
        private async Awaitable ProcessLoadTaskQueueAsync()
        {
            if (totalLoadTaskCount <= 0)
            {
                currentLoadProgressValue = 100.0f;
                return;
            }

            while (loadTaskQueue.Count > 0)
            {
                ILoadTask task = loadTaskQueue.Dequeue();
                _ = RunLoadTaskAsync(task);
            }

            while (completedLoadTaskCount < totalLoadTaskCount)
            {
                await Awaitable.NextFrameAsync();
            }

            currentLoadProgressValue = 100.0f;
        }

        private async Awaitable RunLoadTaskAsync(ILoadTask _task)
        {
            try
            {
                await _task.ExecuteAsync();
            }
            catch (Exception exception)
            {
                // 업무 하나가 예외를 던지더라도 completedLoadTaskCount가 증가하지 않으면
                // ProcessLoadTaskQueueAsync의 대기 루프가 영원히 끝나지 않아 로딩 화면이 멈추므로,
                // 예외를 여기서 흡수하고 완료 처리는 finally에서 항상 수행한다.
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"로드 업무 처리 중 예외 발생 : {exception}");
            }
            finally
            {
                completedLoadTaskCount++;
                UpdateProgressByQueue();
            }
        }

        private void UpdateProgressByQueue()
        {
            if (totalLoadTaskCount <= 0)
            {
                currentLoadProgressValue = 100.0f;
                return;
            }

            currentLoadProgressValue = ((float)completedLoadTaskCount / totalLoadTaskCount) * 100.0f;
        }

        /// <summary>
        /// AddressableAssetModelSO에서 씬 태그에 매칭되는 preload Key 목록을 가져온다.
        /// </summary>
        private List<string> CollectPreloadKeyStrings(string _tagName)
        {
            List<string> keyStrings = new List<string>();

            if (addressableAssetModelDictionary == null || string.IsNullOrEmpty(_tagName))
            {
                return keyStrings;
            }

            if (!addressableAssetModelDictionary.TryGetValue(_tagName, out Incheol.Models.SO.AddressableAssetModel model) || model == null)
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"AddressableAssetModelSO에 tag '{_tagName}'에 대한 preload 설정이 없습니다.");
                return keyStrings;
            }

            List<AddressableAssetKey> preloadKeys = model.preloadAddressableKeys;
            if (preloadKeys == null)
            {
                return keyStrings;
            }

            for (int i = 0; i < preloadKeys.Count; i++)
            {
                AddressableAssetKey key = preloadKeys[i];
                if (key == AddressableAssetKey.None)
                {
                    continue;
                }

                keyStrings.Add(key.ToString());
            }

            return keyStrings;
        }
        #endregion
    }
}
