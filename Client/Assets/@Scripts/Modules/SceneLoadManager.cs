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

        #region Variable
        [SerializeField] Incheol.Models.SO.SceneDataModel currentSceneDataModel;
        private readonly SceneDataRegistry sceneDataRegistry = new SceneDataRegistry();

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

        /// <summary>
        /// 이번 씬 전환의 진행률을 UI_LoadingBarView에 표시하는 중인지. 로비처럼 호출한 씬이 전환 도중 언로드되는 경우,
        /// 호출한 쪽은 로딩바를 숨길 수 없으므로 씬이 유지되는 이 매니저가 표시/진행률/숨김을 모두 맡는다.
        /// </summary>
        private bool reportToLoadingBar;

        /// <summary>전환이 끝나도 로딩바를 숨기지 않고 다음 씬의 Presenter가 이어받아 숨기는 경우 true.</summary>
        private bool keepLoadingBarAfterLoad;

        private const string loadingBarTitle = "게임 데이터를 불러오는 중...";
        #endregion

        #region Method
        /// <summary>
        /// SceneDataModelSO / AddressableAssetModelSO를 Addressable로 로드한다.
        /// 완료 시 _onComplete(true/false)를 반드시 호출하여 호출측이 무한 대기하지 않도록 한다.
        /// </summary>
        public void Init(Action<bool> _onComplete = null)
        {
            IsInitialized = false;
            sceneDataRegistry.Load(success =>
            {
                IsInitialized = success;
                _onComplete?.Invoke(success);
            });
        }

/// <summary>
        /// _showLoadingBar가 true이면 전환이 끝날 때까지 UI_LoadingBarView에 진행률을 표시하고, 진행률이 100%가 되어
        /// 이전 씬 정리까지 끝난 뒤에 숨긴다. 로딩바를 직접 관리하는 씬(Bootstrap/Login 등)은 기본값(false)을 쓴다.
        /// 로딩바를 보여주는 전환은 프리로드를 먼저 모두 끝내 100%를 채운 뒤에 대상 씬을 로드한다(이전 씬이 보이는 동안 대상 씬이
        /// 먼저 나타나지 않도록).
        /// _keepLoadingBar가 true이면 전환이 끝나도 로딩바를 숨기지 않고 100%로 유지한다. 새 씬이 자체 초기화(맵/플레이어 생성,
        /// 서버 입장 등)를 이어서 진행하는 경우에 쓰며, 이때는 새 씬의 Presenter가 반드시 숨겨야 한다.
        /// </summary>
        public void LoadSceneByTags(string _tagName, bool _showLoadingBar = false, bool _keepLoadingBar = false)
        {
            if (!IsInitialized || !sceneDataRegistry.IsLoaded)
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"LoadSceneByTags 호출 전에 Init이 완료되지 않았습니다. tag : {_tagName}");
                return;
            }

            if (string.IsNullOrEmpty(_tagName))
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>("LoadSceneByTags에 빈 tag가 전달되었습니다.");
                return;
            }

            if (!sceneDataRegistry.TryGetScene(_tagName, out Incheol.Models.SO.SceneDataModel targetSceneDataModel))
            {
                DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"존재하지 않는 Scene tag : {_tagName}. 등록된 tags : [{sceneDataRegistry.DescribeSceneTags()}]");
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
            currentSceneDataModel = targetSceneDataModel;
            _ = SceneLoadAsync(previousSceneDataModel, currentSceneDataModel, _showLoadingBar, _keepLoadingBar);
        }

        /// <summary>
        /// 이전/대상 SceneDataModel을 비교해 필요한 씬만 로드하고, 더 이상 필요없는 씬만 언로드한다.
        /// </summary>
private async Awaitable SceneLoadAsync(Incheol.Models.SO.SceneDataModel _previous, Incheol.Models.SO.SceneDataModel _target, bool _showLoadingBar, bool _keepLoadingBar)
        {
            isSceneLoading = true;

            try
            {
                reportToLoadingBar = _showLoadingBar && GameManager.Instance != null;
                keepLoadingBarAfterLoad = reportToLoadingBar && _keepLoadingBar;
                if (reportToLoadingBar)
                {
                    GameManager.Instance.ShowLoadingBar();
                    GameManager.Instance.LoadingBarView?.UpdateTitle(loadingBarTitle);
                }

                await Awaitable.WaitForSecondsAsync(0.2f);

                // 이전 태그 세션에서 로드했던 Addressable 핸들 / 오브젝트 풀 정리 (최초 실행 시 keyDictionary가 비어 있어 no-op)
                HashSet<string> keysToPreserve = ObjectPoolManager.Instance != null ? ObjectPoolManager.Instance.GetTrackedKeys() : null;
                AddressableAssetManager.Instance.ReleaseAllHandler(keysToPreserve);
                //ObjectPoolController.Instance.Init();

                SetLoadProgress(0.0f);
                loadTaskQueue.Clear();
                totalLoadTaskCount = 0;
                completedLoadTaskCount = 0;

                List<string> targetSceneList = _target.loadedSceneList ?? new List<string>();

                // SceneDataModel은 ScriptableObject가 아니라 [Serializable] class라서, Unity가 컴포넌트를
                // 역직렬화할 때 _previous를 항상 (비어있더라도) 실제 인스턴스로 만들어버려 절대 null이 되지 않는다.
                // 그래서 "이전 SceneDataModel이 없다(최초 전환, 예: BootstrapScene)"는 tags가 비어있는지로 판별해야 한다.
                bool hasPreviousModel = _previous != null && !string.IsNullOrEmpty(_previous.tags);

                // 이전 SceneDataModel이 없으면(최초 전환, 예: BootstrapScene) 현재 활성 씬을
                // 정리 대상으로 간주해 diff에 포함시킨다.
                List<string> previousSceneList = hasPreviousModel
                    ? (_previous.loadedSceneList ?? new List<string>())
                    : new List<string> { SceneManager.GetActiveScene().name };

                List<string> scenesToUnload = previousSceneList.Where(sceneName => !targetSceneList.Contains(sceneName)).ToList();

                // 로딩바를 보여주는 전환은 프리로드(Addressable)를 먼저 모두 끝내 100%를 채운 뒤에 대상 씬을 로드한다.
                // 씬 로드를 프리로드와 동시에 시작하면 대상 씬이 로딩 도중에 먼저 활성화되어, 이전 씬(로비)이 보이는 동안
                // 대상 씬의 월드/UI가 같이 나타난다.
                bool loadScenesAfterPreload = reportToLoadingBar;

                EnqueueLoadTasks(_target, !loadScenesAfterPreload);
                await ProcessLoadTaskQueueAsync();

                // Queue가 비워져 Progress가 100이 된 뒤에만 씬 전환
                SetLoadProgress(100.0f);

                if (loadScenesAfterPreload)
                {
                    // 100%가 화면에 보이도록 잠깐 두었다가 씬을 로드한다.
                    await Awaitable.WaitForSecondsAsync(0.2f);
                    await LoadTargetScenesAsync(_target);
                }

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
                if (reportToLoadingBar)
                {
                    reportToLoadingBar = false;

                    // 다음 씬이 이어받는 경우에는 숨기지 않는다(그쪽에서 진행률을 이어 채우고 숨긴다).
                    if (!keepLoadingBarAfterLoad)
                    {
                        // LoadingBarView는 풀에서 재사용되는 인스턴스라 다음 사용처에 이 문구가 남지 않게 비운다.
                        GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);
                        GameManager.Instance?.HideLoadingBar();
                    }

                    keepLoadingBarAfterLoad = false;
                }

                isSceneLoading = false;
            }
        }

        /// <summary>
        /// 진행률(0~100)을 저장하고, 로딩바를 표시 중이면 UI_LoadingBarView(0~1)에도 반영한다.
        /// </summary>
        private void SetLoadProgress(float _percent)
        {
            currentLoadProgressValue = _percent;

            if (reportToLoadingBar)
            {
                GameManager.Instance?.LoadingBarView?.UpdateProgress(_percent / 100.0f);
            }
        }

        /// <summary>
        /// Addressable / Additive 씬 로드 업무를 Queue에 등록한다.
        /// </summary>
        private void EnqueueLoadTasks(Incheol.Models.SO.SceneDataModel _target, bool _includeSceneTasks = true)
        {
            List<string> addressableKeys = sceneDataRegistry.CollectPreloadKeyStrings(currentSceneTag);
            for (int i = 0; i < addressableKeys.Count; i++)
            {
                loadTaskQueue.Enqueue(new AddressableLoadTask(addressableKeys[i]));
            }

            if (_includeSceneTasks)
            {
                List<string> sceneTargets = _target.loadedSceneList ?? new List<string>();
                for (int i = 0; i < sceneTargets.Count; i++)
                {
                    loadTaskQueue.Enqueue(new AdditiveSceneLoadTask(sceneTargets[i], _target.activeSceneName));
                }
            }

            totalLoadTaskCount = loadTaskQueue.Count;
            completedLoadTaskCount = 0;
            UpdateProgressByQueue();
        }

        /// <summary>
        /// 프리로드가 모두 끝난 뒤에 대상 씬들을 순서대로 Additive 로드한다(진행률은 이미 100%).
        /// 한 씬이 실패해도 나머지 정리(이전 씬 언로드, 로딩바 처리)가 이어지도록 예외는 여기서 흡수한다.
        /// </summary>
        private async Awaitable LoadTargetScenesAsync(Incheol.Models.SO.SceneDataModel _target)
        {
            List<string> sceneTargets = _target.loadedSceneList ?? new List<string>();
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                try
                {
                    await new AdditiveSceneLoadTask(sceneTargets[i], _target.activeSceneName).ExecuteAsync();
                }
                catch (Exception exception)
                {
                    DebugLogManager.GenerateErrorMessage<SceneLoadManager>($"대상 씬 로드 중 예외 발생 Scene : {sceneTargets[i]}, Exception : {exception}");
                }
            }
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
                SetLoadProgress(100.0f);
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

            SetLoadProgress(100.0f);
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
                SetLoadProgress(100.0f);
                return;
            }

            SetLoadProgress(((float)completedLoadTaskCount / totalLoadTaskCount) * 100.0f);
        }
        #endregion
    }
}
