using Incheol.Model;
using Incheol.Model.Player;
using Incheol.Model.SO;
using Incheol.Util;
using Incheol.Define;

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Incheol.Module
{
    public class SceneLoadController : SingletonObject<SceneLoadController>
    {
        #region Type
        /// <summary>
        /// 로딩 큐에 들어가는 개별 작업 단위. 완료되면 진행률이 갱신된다.
        /// </summary>
        private class LoadStep
        {
            public string Name { get; }
            public Func<Awaitable> Action { get; }

            public LoadStep(string _name, Func<Awaitable> _action)
            {
                Name = _name;
                Action = _action;
            }
        }
        #endregion

        #region Variable
        [Header("Current Scene Module")]
        [SerializeField] SceneModel currentSceneModel;

        [Header("Load Scene Dictionary")]
        Dictionary<string, SceneModel> dicSceneModels;

        [Header("Current Load Progress")]
        public float cur_LoadProgress = 0.0f;

        /// <summary>
        /// 로딩 스텝이 하나 완료될 때마다 갱신된 진행률(0~1)과 함께 호출된다.
        /// </summary>
        public event Action<float> OnLoadProgressChanged;

        private int loadTotalSteps;
        private int loadCurrentStep;

        private GameObject instantiatedMapInstance;
        private GameObject instantiatedPlayerInstance;

        public const string RegistryKey_CurrentMapRoot = "CurrentMap";
        public const string RegistryKey_PlayerRespawn = "PlayerRespawn";
        public const string RegistryKey_Player = "Player";

        /// <summary>
        /// SceneLoadController가 초기화되었는지 확인하는 프로퍼티
        /// </summary>
        public bool IsInitialized => dicSceneModels != null;
        #endregion

        #region Method
        public void Init(Action _callback)
        {
            Addressables.LoadAssetAsync<SceneModelSO>("SceneModelSO").Completed += (result) =>
            {
                dicSceneModels = new Dictionary<string, SceneModel>();

                List<SceneModel> sceneModels = result.Result.scneneModel_List;

                for (int i = 0; i < sceneModels.Count; i++)
                {
                    if (!dicSceneModels.ContainsKey(sceneModels[i].sceneTag))
                    {
                        dicSceneModels.Add(sceneModels[i].sceneTag, sceneModels[i]);
                    }
                }
                _callback?.Invoke();
            };
        }

        /// <summary>
        /// tag에 해당하는 SceneModel을 조회한다.
        /// 부트스트랩 씬 등 외부에서 전환 대상 씬 구성(로드할 씬 목록/활성 씬)을 확인할 때 사용한다.
        /// </summary>
        public SceneModel GetSceneModel(string _tag)
        {
            if (!IsInitialized || !dicSceneModels.ContainsKey(_tag))
            {
                return null;
            }

            return dicSceneModels[_tag];
        }

        /// <summary>
        /// LoadingScene을 경유하는 일반 씬 전환(Login ↔ Main 등)에서 사용한다.
        /// </summary>
        public async void LoadSceneByTags(string _tag)
        {
            try
            {
                if (!IsInitialized)
                {
                    return;
                }

                if (!dicSceneModels.ContainsKey(_tag))
                {
                    Utils.CreateLogMessage<SceneLoadController>($"{_tag}는 존재하지 않습니다.");
                    return;
                }

                currentSceneModel = dicSceneModels[_tag];
                await SceneLoadCoreAsync(currentSceneModel);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 순수 Scene 전환만 담당한다.
        /// 전환 전 준비 작업(에셋 로딩 등)과 그 진행률 관리는 호출측(DummySceneController 등)이 책임진다.
        /// 대상 씬들을 additive로 로드한 뒤 필요 시 활성 씬을 지정하고, 마지막으로 지정된 씬을 언로드한다.
        /// </summary>
        public async Awaitable TransitionScenesAsync(List<string> _scenesToLoad, string _activeScene, string _sceneToUnload)
        {
            ResetTransitionState();

            for (int i = 0; i < _scenesToLoad.Count; i++)
            {
                string sceneName = _scenesToLoad[i];
                AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                while (!asyncLoad.isDone)
                {
                    await Awaitable.NextFrameAsync();
                }

                if (_activeScene == sceneName)
                {
                    UnityEngine.SceneManagement.Scene targetActiveScene = SceneManager.GetSceneByName(sceneName);
                    SceneManager.SetActiveScene(targetActiveScene);
                }
            }

            if (!string.IsNullOrEmpty(_sceneToUnload))
            {
                await UnloadSceneStepAsync(_sceneToUnload);
            }
        }

        private void ResetTransitionState()
        {
            RuntimeObjectRegistry.Instance.Clear();
            instantiatedMapInstance = null;
            instantiatedPlayerInstance = null;
        }

        private async Awaitable SceneLoadCoreAsync(SceneModel _targetModel)
        {
            bool isMainLoad = _targetModel.sceneTag == "Main";
            List<string> sceneTarget = _targetModel.loadScenes;

            ResetTransitionState();

            Queue<LoadStep> loadStepQueue = BuildLoadStepQueue(_targetModel, isMainLoad, sceneTarget);
            ResetLoadProgress(loadStepQueue.Count);

            while (loadStepQueue.Count > 0)
            {
                LoadStep step = loadStepQueue.Dequeue();
                await step.Action();
                CompleteLoadStep();
            }
        }

        /// <summary>
        /// 이번 전환에서 처리할 로딩 스텝을 순서대로 Queue에 담는다.
        /// </summary>
        private Queue<LoadStep> BuildLoadStepQueue(
            SceneModel _targetModel,
            bool _isMainLoad,
            List<string> _sceneTarget)
        {
            Queue<LoadStep> stepQueue = new Queue<LoadStep>();

            stepQueue.Enqueue(new LoadStep("LoadingScene 로드", LoadLoadingSceneStepAsync));

            for (int i = 0; i < _sceneTarget.Count; i++)
            {
                string sceneName = _sceneTarget[i];
                stepQueue.Enqueue(new LoadStep($"{sceneName} 씬 로드", () => LoadAdditiveSceneStepAsync(sceneName, _targetModel)));
            }

            if (_isMainLoad)
            {
                stepQueue.Enqueue(new LoadStep("BeginnerVillage 맵 로드", LoadMainMapStepAsync));
                stepQueue.Enqueue(new LoadStep("PlayerPrefab 생성", SpawnPlayerStepAsync));
                stepQueue.Enqueue(new LoadStep($"{AddressKey.UI_MainScene} UI 로드", () => LoadMainUIStepAsync(AddressKey.UI_MainScene.ToString())));
                stepQueue.Enqueue(new LoadStep($"{AddressKey.UI_InventoryViewPopup} UI 로드", () => LoadMainUIStepAsync(AddressKey.UI_InventoryViewPopup.ToString(), false)));
                stepQueue.Enqueue(new LoadStep($"{AddressKey.UI_CharacterInfoVIewPopup} UI 로드", () => LoadMainUIStepAsync(AddressKey.UI_CharacterInfoVIewPopup.ToString(), false)));
            }

            stepQueue.Enqueue(new LoadStep("LoadingScene 언로드", UnloadLoadingSceneStepAsync));

            return stepQueue;
        }

        private void ResetLoadProgress(int _totalSteps)
        {
            loadTotalSteps = Mathf.Max(1, _totalSteps);
            loadCurrentStep = 0;
            cur_LoadProgress = 0f;
        }

        /// <summary>
        /// 스텝 하나가 완료될 때마다 호출되어 cur_LoadProgress를 갱신하고 구독자에게 통지한다.
        /// </summary>
        private void CompleteLoadStep()
        {
            loadCurrentStep++;
            cur_LoadProgress = Mathf.Clamp01((float)loadCurrentStep / loadTotalSteps);
            OnLoadProgressChanged?.Invoke(cur_LoadProgress);
        }

        private async Awaitable LoadLoadingSceneStepAsync()
        {
            AsyncOperation asyncLoadingScene = SceneManager.LoadSceneAsync(DEFINE.LOADING_SCENE, LoadSceneMode.Single);
            while (!asyncLoadingScene.isDone)
            {
                await Awaitable.NextFrameAsync();
            }
        }

        private async Awaitable LoadAdditiveSceneStepAsync(string _sceneName, SceneModel _targetModel)
        {
            AsyncOperation async = SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Additive);
            while (!async.isDone)
            {
                await Awaitable.NextFrameAsync();
            }

            if (_targetModel.activeScene == _sceneName)
            {
                UnityEngine.SceneManagement.Scene targetActiveScene = SceneManager.GetSceneByName(_sceneName);
                SceneManager.SetActiveScene(targetActiveScene);
            }
        }

        private Awaitable UnloadLoadingSceneStepAsync()
        {
            return UnloadSceneStepAsync(DEFINE.LOADING_SCENE);
        }

        private async Awaitable UnloadSceneStepAsync(string _sceneName)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(_sceneName);
            if (unloadOp == null)
            {
                return;
            }

            while (!unloadOp.isDone)
            {
                await Awaitable.NextFrameAsync();
            }
        }

        private async Awaitable LoadMainMapStepAsync()
        {
            GameObject currentMapRoot = GameObject.Find(RegistryKey_CurrentMapRoot);
            if (currentMapRoot != null)
            {
                RuntimeObjectRegistry.Instance.Register(RegistryKey_CurrentMapRoot, currentMapRoot);
            }

            await AddressableController.Instance.InstantiateAsset(
                AddressKey.BeginnerVillage.ToString(),
                currentMapRoot,
                OnMapInstantiated);
        }

        private void OnMapInstantiated(GameObject _mapInstance)
        {
            instantiatedMapInstance = _mapInstance;
            if (_mapInstance == null)
            {
                Utils.CreateLogMessage<SceneLoadController>("BeginnerVillage 맵 생성에 실패했습니다.");
                return;
            }

            RuntimeObjectRegistry.Instance.Register(AddressKey.BeginnerVillage.ToString(), _mapInstance);
            EnsurePlayerRespawnRegistered(_mapInstance);
        }

        /// <summary>
        /// 맵 생성 시점에 PlayerRespawn을 마커/레지스트리에 등록한다.
        /// </summary>
        private void EnsurePlayerRespawnRegistered(GameObject _mapInstance)
        {
            PlayerRespawnPoint respawnPoint = _mapInstance.GetComponentInChildren<PlayerRespawnPoint>(true);
            if (respawnPoint == null)
            {
                Transform[] transforms = _mapInstance.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name == RegistryKey_PlayerRespawn)
                    {
                        respawnPoint = transforms[i].gameObject.AddComponent<PlayerRespawnPoint>();
                        break;
                    }
                }
            }

            if (respawnPoint != null)
            {
                RuntimeObjectRegistry.Instance.Register(RegistryKey_PlayerRespawn, respawnPoint.gameObject);
            }
            else
            {
                Utils.CreateLogMessage<SceneLoadController>("PlayerRespawn을 맵에서 찾지 못했습니다.");
            }
        }

        private async Awaitable SpawnPlayerStepAsync()
        {
            GameObject currentMapRoot = RuntimeObjectRegistry.Instance.Get(RegistryKey_CurrentMapRoot);
            GameObject playerRespawnGo = RuntimeObjectRegistry.Instance.Get(RegistryKey_PlayerRespawn);

            if (playerRespawnGo != null)
            {
                await AddressableController.Instance.InstantiateAsset(
                    AddressKey.PlayerPrefab.ToString(),
                    currentMapRoot,
                    go => OnPlayerInstantiated(go, playerRespawnGo.transform));
            }
            else
            {
                Utils.CreateLogMessage<SceneLoadController>("PlayerRespawn이 등록되지 않아 PlayerPrefab을 생성하지 않습니다.");
            }
        }

        private void OnPlayerInstantiated(GameObject _playerGo, Transform _respawnTr)
        {
            instantiatedPlayerInstance = _playerGo;
            if (_playerGo == null || _respawnTr == null)
            {
                Utils.CreateLogMessage<SceneLoadController>("PlayerPrefab 생성에 실패했습니다.");
                return;
            }

            _playerGo.transform.position = _respawnTr.position;
            _playerGo.transform.rotation = _respawnTr.rotation;

            ApplyLoggedInUserToPlayer(_playerGo.transform);
            RuntimeObjectRegistry.Instance.Register(RegistryKey_Player, _playerGo);
            RuntimeObjectRegistry.Instance.Unregister(RegistryKey_PlayerRespawn);

            Destroy(_respawnTr.gameObject);
        }

        private async Awaitable LoadMainUIStepAsync(string _uiKey, bool _active = true)
        {
            GameObject mainSceneRoot = GameObject.Find("@MainScene");
            if (mainSceneRoot != null)
            {
                await LoadAndInstantiateAddressableUIAsync(_uiKey, mainSceneRoot, _active);
            }
            else
            {
                Utils.CreateLogMessage<SceneLoadController>("@MainScene 오브젝트를 찾지 못했습니다. Main UI를 생성하지 않습니다.");
            }
        }

        private void ApplyLoggedInUserToPlayer(Transform _playerTr)
        {
            string userName = GameManager.LoggedInUserName;
            if (string.IsNullOrEmpty(userName) || _playerTr == null)
            {
                return;
            }

            _playerTr.name = userName;

            PlayerModel playerModel = _playerTr.GetComponent<PlayerModel>();
            if (playerModel != null)
            {
                playerModel.SetUserName(userName);
            }
        }

        /// <summary>
        /// InstantiateAsync 콜백으로 인스턴스를 받아 활성 상태 설정 후 Registry에 등록한다.
        /// </summary>
        private async Awaitable LoadAndInstantiateAddressableUIAsync(
            string _key,
            GameObject _parent,
            bool _active = true)
        {
            GameObject created = null;

            if (_key == AddressKey.UI_MainScene.ToString())
            {
                // 프리로드된 에셋 핸들은 유지하고 일반 복제본을 생성한다.
                // 화면 전환 시 복제본은 Destroy되지만 Addressables.Release는 호출하지 않는다.
                created = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(
                    _key,
                    _parent.transform);
            }
            else
            {
                await AddressableController.Instance.InstantiateAsset(
                    _key,
                    _parent,
                    go => created = go);
            }

            if (created == null)
            {
                Utils.CreateLogMessage<SceneLoadController>($"{_key} UI 생성에 실패했습니다.");
                return;
            }

            created.SetActive(_active);
            RuntimeObjectRegistry.Instance.Register(_key, created);
            Utils.CreateLogMessage<SceneLoadController>($"{_key} UI 생성 완료 (active={_active})");
        }
        #endregion
    }
}
