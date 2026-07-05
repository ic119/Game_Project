using JJORY.Model;
using JJORY.Model.Player;
using JJORY.Model.SO;
using JJORY.Util;
using JJORY.Define;

using System;
using System.Collections;
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

        public void LoadSceneByTags(string _tag)
        {
            // dicSceneModels가 null이거나 초기화되지 않은 경우 체크
            if (!IsInitialized)
            {
                return;
            }

            if (!dicSceneModels.ContainsKey(_tag))
            {
                Utils.CreateLogMessage<SceneLoadController>($"{_tag}는 존재하지 않습니다.");
            }
            else
            {
                currentSceneModel = dicSceneModels[_tag];

                StartCoroutine(SceneLoadRoutine(currentSceneModel));
            }
        }

        /// <summary>
        /// Scene 전환 코루틴 함수
        /// </summary>
        private IEnumerator SceneLoadRoutine(SceneModel _targetModel)
        {
            bool isMainLoad = _targetModel.sceneTag == "Main";
            List<string> sceneTarget = _targetModel.loadScenes;
            int mainAssetStepCount = isMainLoad ? 5 : 0; // Map, Player, UI x3

            ResetLoadProgress(1 + sceneTarget.Count + mainAssetStepCount);

            // Step 1. LoadingScene 로드
            BeginLoadStep("LoadingScene 로드");
            UpdateLoadProgress(0f);
            AsyncOperation asyncLoadingScene = SceneManager.LoadSceneAsync(DEFINE.LOADING_SCENE, LoadSceneMode.Single);
            while (!asyncLoadingScene.isDone)
            {
                UpdateLoadProgress(asyncLoadingScene.progress / 0.9f);
                yield return null;
            }
            CompleteLoadStep("LoadingScene 로드");

            // Step 2. 대상 씬 Additive 로드
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
                    yield return null;
                }

                if (_targetModel.activeScene == sceneTarget[i])
                {
                    targetActiveScene = SceneManager.GetSceneByName(sceneTarget[i]);
                    SceneManager.SetActiveScene(targetActiveScene);
                }

                CompleteLoadStep(sceneStepName);
            }

            // Step 3~7. MainScene 전용 에셋 로드
            if (isMainLoad)
            {
                yield return LoadMainMapStep();
                yield return SpawnPlayerStep();
                yield return LoadMainUIStep(AddressKey.UI_MainScene.ToString());
                yield return LoadMainUIStep(AddressKey.UI_InventoryViewPopup.ToString(), false);
                yield return LoadMainUIStep(AddressKey.UI_CharacterInfoVIewPopup.ToString(), false);
            }

            // 100% 완료 후 LoadingScene 언로드 → MainScene(또는 대상 씬)으로 전환
            cur_LoadProgress = 1.0f;
            LogLoadProgress("모든 작업 완료 - LoadingScene 언로드 시작");
            yield return null;

            AsyncOperation unloadLoadingScene = SceneManager.UnloadSceneAsync(DEFINE.LOADING_SCENE);
            yield return new WaitUntil(() => unloadLoadingScene.isDone);

            LogLoadProgress("LoadingScene 언로드 완료 - 대상 씬 전환");

            UIController.Instance.OpenMask();
            yield return new WaitForSeconds(1.0f);
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
        }

        /// <summary>
        /// MainScene 맵(BeginnerVillage) 로드 단계
        /// </summary>
        private IEnumerator LoadMainMapStep()
        {
            BeginLoadStep("BeginnerVillage 맵 로드");
            UpdateLoadProgress(0f);

            AddressableController.Instance.LoadPrefabAddress<GameObject>(AddressKey.BeginnerVillage.ToString());
            GameObject currentMap = GameObject.Find("CurrentMap");
            yield return AddressableController.Instance.InstantiateAsset(AddressKey.BeginnerVillage.ToString(), currentMap);

            CompleteLoadStep("BeginnerVillage 맵 로드");
        }

        /// <summary>
        /// PlayerPrefab 생성 및 배치 단계
        /// </summary>
        private IEnumerator SpawnPlayerStep()
        {
            BeginLoadStep("PlayerPrefab 생성");
            UpdateLoadProgress(0f);

            GameObject currentMap = GameObject.Find("CurrentMap");
            GameObject mapInstance = null;

            if (currentMap != null && currentMap.transform.childCount > 0)
            {
                mapInstance = currentMap.transform.GetChild(currentMap.transform.childCount - 1).gameObject;
            }

            Transform playerRespawnTr = null;
            if (mapInstance != null)
            {
                playerRespawnTr = FindInChildren(mapInstance.transform, "PlayerRespawn");
            }

            if (playerRespawnTr == null)
            {
                GameObject findBackup = GameObject.Find("PlayerRespawn");
                if (findBackup != null)
                {
                    playerRespawnTr = findBackup.transform;
                }
            }

            if (playerRespawnTr != null)
            {
                AddressableController.Instance.LoadPrefabAddress<GameObject>(AddressKey.PlayerPrefab.ToString());
                int childCountBeforeSpawn = currentMap != null ? currentMap.transform.childCount : -1;
                yield return AddressableController.Instance.InstantiateAsset(AddressKey.PlayerPrefab.ToString(), currentMap);

                bool isPlayerSpawnSuccess = currentMap != null &&
                                            childCountBeforeSpawn >= 0 &&
                                            currentMap.transform.childCount > childCountBeforeSpawn;

                if (isPlayerSpawnSuccess)
                {
                    Transform playerTr = currentMap.transform.GetChild(currentMap.transform.childCount - 1);
                    playerTr.position = playerRespawnTr.position;
                    playerTr.rotation = playerRespawnTr.rotation;

                    ApplyLoggedInUserToPlayer(playerTr);

                    Destroy(playerRespawnTr.gameObject);
                }
            }

            CompleteLoadStep("PlayerPrefab 생성");
        }

        /// <summary>
        /// MainScene UI 프리팹 로드 단계
        /// </summary>
        private IEnumerator LoadMainUIStep(string _uiKey, bool _active = true)
        {
            string stepName = $"{_uiKey} UI 로드";
            BeginLoadStep(stepName);
            UpdateLoadProgress(0f);

            GameObject mainSceneRoot = GameObject.Find("@MainScene");
            if (mainSceneRoot != null)
            {
                yield return LoadAndInstantiateAddressableUI(_uiKey, mainSceneRoot, _active);
            }

            CompleteLoadStep(stepName);
        }

        /// <summary>
        /// 로그인 시 저장한 계정명을 PlayerPrefab 이름 및 PlayerModel.userName에 반영한다.
        /// </summary>
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
        /// Addressable UI 프리팹을 로드한 뒤 지정 부모 하위에 생성한다.
        /// </summary>
        private IEnumerator LoadAndInstantiateAddressableUI(string _key, GameObject _parent, bool _active = true)
        {
            AddressableController.Instance.LoadPrefabAddress<GameObject>(_key);
            yield return AddressableController.Instance.InstantiateAsset(_key, _parent);

            Transform uiTransform = FindInChildren(_parent.transform, _key);
            if (uiTransform != null)
            {
                uiTransform.gameObject.SetActive(_active);
            }
        }

        /// <summary>
        /// 자식 계층에서 이름으로 Transform 검색
        /// </summary>
        private Transform FindInChildren(Transform _parent, string _name)
        {
            if (_parent.name == _name) return _parent;
            for (int i = 0; i < _parent.childCount; i++)
            {
                Transform found = FindInChildren(_parent.GetChild(i), _name);
                if (found != null) return found;
            }
            return null;
        }
        #endregion
    }
}
