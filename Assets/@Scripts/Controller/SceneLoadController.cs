using JJORY.Model;
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
                Utils.CreateLogMessage<SceneLoadController>($"SceneLoadController가 아직 초기화되지 않았습니다. Init() 함수를 먼저 호출해주세요.");
                return;
            }

            if (!dicSceneModels.ContainsKey(_tag))
            {
                Utils.CreateLogMessage<SceneLoadController>($"{_tag}는 존재하지 않습니다.");
            }
            else
            {
                currentSceneModel = dicSceneModels[_tag];
                Utils.CreateLogMessage<SceneLoadController>($"현재 SceneModel은 {currentSceneModel.ToString()}");

                StartCoroutine(SceneLoadRoutine(currentSceneModel));
            }
        }

        /// <summary>
        /// Scene 전환 코루틴 함수
        /// </summary>
        /// <param name="_targetModel"></param>
        /// <returns></returns>
        private IEnumerator SceneLoadRoutine(SceneModel _targetModel)
        {
            cur_LoadProgress = 0.0f;

            // LoadingScene을 Single 모드로 로드 (기존 씬들을 모두 대체)
            AsyncOperation asyncLoadingScene = SceneManager.LoadSceneAsync(DEFINE.LOADING_SCENE, LoadSceneMode.Single);
            yield return new WaitUntil(() => asyncLoadingScene.isDone);

            UnityEngine.SceneManagement.Scene targetActiveScene = new UnityEngine.SceneManagement.Scene();

            List<string> sceneTarget = _targetModel.loadScenes;
            int count = 0;
            while (count < sceneTarget.Count)
            {
                AsyncOperation async = SceneManager.LoadSceneAsync(sceneTarget[count], LoadSceneMode.Additive);
                
                //전부 로드 할때 까지 대기합니다.
                while (!async.isDone)
                {
                    cur_LoadProgress = ((float)count / (float)sceneTarget.Count) + (1.0f / (float)sceneTarget.Count) * async.progress;
                    yield return null;
                }
                if (_targetModel.activeScene == sceneTarget[count])
                {
                    targetActiveScene = SceneManager.GetSceneByName(sceneTarget[count]);
                    //액티브 씬을 바꾸어 준다.
                    SceneManager.SetActiveScene(targetActiveScene);
                }
                count++;
                cur_LoadProgress = (float)count / (float)sceneTarget.Count;
                yield return new WaitForSeconds(1.0f);
            }
            
            // Main 씬으로 전환될 때, BeginnerVillage 맵과 PlayerPrefab을 Addressable로 생성
            if (currentSceneModel != null && currentSceneModel.sceneTag == "Main")
            {
                // 1) 맵 Addressable 로드 시작
                AddressableController.Instance.LoadPrefabAddress<GameObject>(AddressKey.BeginnerVillage.ToString());

                // 맵이 붙을 부모 오브젝트 (예: "CurrentMap")
                GameObject currentMap = GameObject.Find("CurrentMap");

                // 2) 맵 Instantiate (부모가 없으면 월드 루트에 생성)
                yield return AddressableController.Instance.InstantiateAsset(AddressKey.BeginnerVillage.ToString(), currentMap);

                // 3) PlayerRespawn 오브젝트 찾기 (CurrentMap 아래 우선 검색)
                GameObject playerRespawn = null;
                if (currentMap != null)
                {
                    Transform respawnTr = currentMap.transform.Find("PlayerRespawn");
                    if (respawnTr != null)
                    {
                        playerRespawn = respawnTr.gameObject;
                    }
                }
                // 혹시 못 찾았을 경우 전체 씬에서 검색 (백업용)
                if (playerRespawn == null)
                {
                    playerRespawn = GameObject.Find("PlayerRespawn");
                }

                // 4) 플레이어 프리팹 Addressable 로드 및 Instantiate
                if (playerRespawn != null)
                {
                    AddressableController.Instance.LoadPrefabAddress<GameObject>(AddressKey.PlayerPrefab.ToString());
                    yield return AddressableController.Instance.InstantiateAsset(AddressKey.PlayerPrefab.ToString(), playerRespawn);
                }
                else
                {
                    Utils.CreateLogMessage<SceneLoadController>($"PlayerRespawn 오브젝트를 찾지 못했습니다. PlayerPrefab을 생성하지 않습니다.");
                }
            }

            // 맵 생성까지 완료되면 로딩 100% 처리 후 로딩 씬 언로드
            cur_LoadProgress = 1.0f;

            AsyncOperation UnloadLoadingScene = SceneManager.UnloadSceneAsync(DEFINE.LOADING_SCENE);
            yield return new WaitUntil(() => UnloadLoadingScene.isDone);

            UIController.Instance.OpenMask();
            yield return new WaitForSeconds(1.0f);
        }
        #endregion
    }
}
