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
            
            if (currentSceneModel != null && currentSceneModel.sceneTag == "Main")
            {
                AddressableController.Instance.LoadPrefabAddress<GameObject>(AddressKey.BeginnerVillage.ToString());
                GameObject currentMap = GameObject.Find("CurrentMap");
                yield return AddressableController.Instance.InstantiateAsset(AddressKey.BeginnerVillage.ToString(), currentMap);

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
                    if (findBackup != null) playerRespawnTr = findBackup.transform;
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
                    else
                    {
                        Utils.CreateLogMessage<SceneLoadController>($"PlayerPrefab 생성에 실패하여 PlayerRespawn을 제거하지 않습니다.");
                    }
                }
                else
                {
                    Utils.CreateLogMessage<SceneLoadController>($"PlayerRespawn 오브젝트를 찾지 못했습니다. PlayerPrefab을 생성하지 않습니다.");
                }
            }

            cur_LoadProgress = 1.0f;

            AsyncOperation UnloadLoadingScene = SceneManager.UnloadSceneAsync(DEFINE.LOADING_SCENE);
            yield return new WaitUntil(() => UnloadLoadingScene.isDone);

            UIController.Instance.OpenMask();
            yield return new WaitForSeconds(1.0f);
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
                Utils.CreateLogMessage<SceneLoadController>($"PlayerPrefab 생성 완료. userName={userName}");
            }
            else
            {
                Utils.CreateLogMessage<SceneLoadController>("PlayerPrefab에 PlayerModel 컴포넌트가 없습니다.");
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
