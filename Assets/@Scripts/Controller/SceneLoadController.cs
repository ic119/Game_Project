using JJORY.Model;
using JJORY.Model.Player;
using JJORY.Model.SO;
using JJORY.Util;
using JJORY.Define;

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace JJORY.Module
{
    public class SceneLoadController : SingletonObject<SceneLoadController>
    {
        #region Variable
        [Header("Current Scene Module")]
        [SerializeField] SceneModel currentSceneModel;

        [Header("Load Scene Dictionary")]
        Dictionary<string, SceneModel> dicSceneModels;

        [Header("Current Load Progress")]
        public float cur_LoadProgress = 0.0f;

        private int loadTotalSteps;
        private int loadCurrentStep;
        private int lastLoggedProgressPercent = -1;
        private string currentStepName = string.Empty;

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
                await SceneLoadAsync(currentSceneModel);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async Awaitable SceneLoadAsync(SceneModel _targetModel)
        {
            bool isMainLoad = _targetModel.sceneTag == "Main";
            List<string> sceneTarget = _targetModel.loadScenes;
            int mainAssetStepCount = isMainLoad ? 5 : 0;

            RuntimeObjectRegistry.Instance.Clear();
            instantiatedMapInstance = null;
            instantiatedPlayerInstance = null;

            ResetLoadProgress(1 + sceneTarget.Count + mainAssetStepCount);

            BeginLoadStep("LoadingScene 로드");
            UpdateLoadProgress(0f);
            AsyncOperation asyncLoadingScene = SceneManager.LoadSceneAsync(DEFINE.LOADING_SCENE, LoadSceneMode.Single);
            while (!asyncLoadingScene.isDone)
            {
                UpdateLoadProgress(asyncLoadingScene.progress / 0.9f);
                await Awaitable.NextFrameAsync();
            }
            CompleteLoadStep("LoadingScene 로드");

            UnityEngine.SceneManagement.Scene targetActiveScene = new UnityEngine.SceneManagement.Scene();
            for (int i = 0; i < sceneTarget.Count; i++)
            {
                string sceneStepName = $"{sceneTarget[i]} 씬 로드";
                BeginLoadStep(sceneStepName);
                UpdateLoadProgress(0f);
                AsyncOperation async = SceneManager.LoadSceneAsync(sceneTarget[i], LoadSceneMode.Additive);

                while (!async.isDone)
                {
                    UpdateLoadProgress(async.progress / 0.9f);
                    await Awaitable.NextFrameAsync();
                }

                if (_targetModel.activeScene == sceneTarget[i])
                {
                    targetActiveScene = SceneManager.GetSceneByName(sceneTarget[i]);
                    SceneManager.SetActiveScene(targetActiveScene);
                }

                CompleteLoadStep(sceneStepName);
            }

            if (isMainLoad)
            {
                await LoadMainMapStepAsync();
                await SpawnPlayerStepAsync();
                await LoadMainUIStepAsync(AddressKey.UI_MainScene.ToString());
                await LoadMainUIStepAsync(AddressKey.UI_InventoryViewPopup.ToString(), false);
                await LoadMainUIStepAsync(AddressKey.UI_CharacterInfoVIewPopup.ToString(), false);
            }

            cur_LoadProgress = 1.0f;
            LogLoadProgress("모든 작업 완료 - LoadingScene 언로드 시작");
            await Awaitable.NextFrameAsync();

            AsyncOperation unloadLoadingScene = SceneManager.UnloadSceneAsync(DEFINE.LOADING_SCENE);
            while (!unloadLoadingScene.isDone)
            {
                await Awaitable.NextFrameAsync();
            }

            LogLoadProgress("LoadingScene 언로드 완료 - 대상 씬 전환");

            //UIController.Instance.OpenMask();
            await Awaitable.WaitForSecondsAsync(1.0f);
        }

        private void ResetLoadProgress(int _totalSteps)
        {
            loadTotalSteps = Mathf.Max(1, _totalSteps);
            loadCurrentStep = 0;
            cur_LoadProgress = 0f;
            lastLoggedProgressPercent = -1;
            currentStepName = "로딩 준비";

            LogLoadProgress($"로딩 초기화 (총 {loadTotalSteps}단계)");
        }

        private void BeginLoadStep(string _stepName)
        {
            currentStepName = _stepName;
            LogLoadProgress($"시작 - {_stepName}");
        }

        private void UpdateLoadProgress(float _innerProgress = 0f)
        {
            float stepStart = (float)loadCurrentStep / loadTotalSteps;
            float stepEnd = (float)(loadCurrentStep + 1) / loadTotalSteps;
            cur_LoadProgress = Mathf.Clamp01(stepStart + (stepEnd - stepStart) * Mathf.Clamp01(_innerProgress));
            LogLoadProgressIfChanged();
        }

        private void CompleteLoadStep(string _stepName)
        {
            loadCurrentStep++;
            cur_LoadProgress = Mathf.Clamp01((float)loadCurrentStep / loadTotalSteps);
            lastLoggedProgressPercent = Mathf.RoundToInt(cur_LoadProgress * 100f);
            LogLoadProgress($"완료 - {_stepName}");
        }

        private void LogLoadProgressIfChanged()
        {
            int progressPercent = Mathf.RoundToInt(cur_LoadProgress * 100f);
            if (progressPercent == lastLoggedProgressPercent)
            {
                return;
            }

            lastLoggedProgressPercent = progressPercent;
            LogLoadProgress(currentStepName);
        }

        private void LogLoadProgress(string _message)
        {
            int progressPercent = Mathf.RoundToInt(cur_LoadProgress * 100f);
            int currentStep = Mathf.Clamp(loadCurrentStep + 1, 1, loadTotalSteps);
            //Utils.CreateLogMessage<SceneLoadController>($"[로딩 {progressPercent}%] ({currentStep}/{loadTotalSteps}) {_message}");
        }

        private async Awaitable LoadMainMapStepAsync()
        {
            BeginLoadStep("BeginnerVillage 맵 로드");
            UpdateLoadProgress(0f);

            GameObject currentMapRoot = GameObject.Find(RegistryKey_CurrentMapRoot);
            if (currentMapRoot != null)
            {
                RuntimeObjectRegistry.Instance.Register(RegistryKey_CurrentMapRoot, currentMapRoot);
            }

            await AddressableController.Instance.InstantiateAsset(
                AddressKey.BeginnerVillage.ToString(),
                currentMapRoot,
                OnMapInstantiated);

            CompleteLoadStep("BeginnerVillage 맵 로드");
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
            BeginLoadStep("PlayerPrefab 생성");
            UpdateLoadProgress(0f);

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

            CompleteLoadStep("PlayerPrefab 생성");
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
            string stepName = $"{_uiKey} UI 로드";
            BeginLoadStep(stepName);
            UpdateLoadProgress(0f);

            GameObject mainSceneRoot = GameObject.Find("@MainScene");
            if (mainSceneRoot != null)
            {
                await LoadAndInstantiateAddressableUIAsync(_uiKey, mainSceneRoot, _active);
            }
            else
            {
                Utils.CreateLogMessage<SceneLoadController>("@MainScene 오브젝트를 찾지 못했습니다. Main UI를 생성하지 않습니다.");
            }

            CompleteLoadStep(stepName);
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
