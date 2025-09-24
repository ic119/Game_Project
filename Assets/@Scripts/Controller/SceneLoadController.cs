using JJORY.Model;
using JJORY.Model.SO;
using JJORY.Util;
using JJORY.Define;
using JJORY.Controller.UI;

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
        /// <param name="_targetModel"></param>
        /// <returns></returns>
        private IEnumerator SceneLoadRoutine(SceneModel _targetModel)
        {
            UIController.Instance.CloseMask();
            yield return new WaitForSeconds(1.0f);
            cur_LoadProgress = 0.0f;

            // 기존 씬들을 정리 (LoadingScene 제외)
            yield return StartCoroutine(UnloadAllScenesExceptLoading());

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
                    SceneManager.SetActiveScene(targetActiveScene);
                }
                count++;
                cur_LoadProgress = (float)count / (float)sceneTarget.Count;
                yield return new WaitForSeconds(1.0f);
            }

            AsyncOperation UnloadLoadingScene = SceneManager.UnloadSceneAsync(DEFINE.LOADING_SCENE);
            yield return new WaitUntil(() => UnloadLoadingScene.isDone);


            cur_LoadProgress = 1.0f;

            UIController.Instance.OpenMask();
            yield return new WaitForSeconds(1.0f);
        }

        /// <summary>
        /// LoadingScene을 제외한 모든 씬을 언로드하는 메서드
        /// </summary>
        /// <returns></returns>
        private IEnumerator UnloadAllScenesExceptLoading()
        {
            Utils.CreateLogMessage<SceneLoadController>("씬 언로드 시작");
            
            // 씬 목록을 안전하게 수집
            List<UnityEngine.SceneManagement.Scene> scenesToUnload = new List<UnityEngine.SceneManagement.Scene>();
            
            // 씬 수집 단계
            int sceneCount = SceneManager.sceneCount;
            Utils.CreateLogMessage<SceneLoadController>($"현재 로드된 씬 수: {sceneCount}");
            
            // 모든 씬을 확인하여 LoadingScene이 아닌 씬들을 수집
            for (int i = 0; i < sceneCount; i++)
            {
                try
                {
                    UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneAt(i);
                    
                    // 씬이 유효하고 로드되어 있으며, LoadingScene이 아닌 경우
                    if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.name) && scene.name != DEFINE.LOADING_SCENE)
                    {
                        Utils.CreateLogMessage<SceneLoadController>($"언로드 대상 씬 발견: {scene.name}");
                        scenesToUnload.Add(scene);
                    }
                }
                catch (System.Exception e)
                {
                    Utils.CreateLogMessage<SceneLoadController>($"씬 {i} 접근 중 오류: {e.Message}");
                }
            }
            
            Utils.CreateLogMessage<SceneLoadController>($"언로드할 씬 수: {scenesToUnload.Count}");
            
            // 각 씬의 Addressable 에셋들을 먼저 정리
            foreach (var scene in scenesToUnload)
            {
                yield return StartCoroutine(CleanupSceneAddressables(scene));
            }
            
            // 수집된 씬들을 언로드
            List<AsyncOperation> unloadOperations = new List<AsyncOperation>();
            
            foreach (var scene in scenesToUnload)
            {
                try
                {
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        Utils.CreateLogMessage<SceneLoadController>($"언로드 중인 씬: {scene.name}");
                        AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(scene);
                        
                        if (unloadOp != null)
                        {
                            unloadOperations.Add(unloadOp);
                        }
                        else
                        {
                            Utils.CreateLogMessage<SceneLoadController>($"씬 {scene.name} 언로드 작업 생성 실패");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Utils.CreateLogMessage<SceneLoadController>($"씬 {scene.name} 언로드 중 오류: {e.Message}");
                }
            }
            
            // 모든 언로드 작업이 완료될 때까지 대기
            foreach (AsyncOperation op in unloadOperations)
            {
                if (op != null)
                {
                    yield return new WaitUntil(() => op.isDone);
                }
            }
            
            Utils.CreateLogMessage<SceneLoadController>("기존 씬들 언로드 완료");
        }

        /// <summary>
        /// 씬의 Addressable 에셋들을 정리하는 메서드
        /// </summary>
        /// <param name="_scene"></param>
        /// <returns></returns>
        private IEnumerator CleanupSceneAddressables(UnityEngine.SceneManagement.Scene _scene)
        {
            Utils.CreateLogMessage<SceneLoadController>($"씬 {_scene.name}의 Addressable 에셋 정리 시작");
            
            try
            {
                // 씬 내의 모든 GameObject를 확인
                GameObject[] rootObjects = _scene.GetRootGameObjects();
                
                foreach (GameObject rootObj in rootObjects)
                {
                    // UI_LoginSceneController 컴포넌트가 있는지 확인
                    var loginController = rootObj.GetComponentInChildren<UI_LoginSceneController>();
                    if (loginController != null)
                    {
                        Utils.CreateLogMessage<SceneLoadController>($"UI_LoginSceneController 발견, 정리 중...");
                        
                        // 이벤트 리스너 정리 (OnDestroy가 호출되지 않을 수 있으므로)
                        var buttons = loginController.GetComponentsInChildren<UnityEngine.UI.Button>();
                        foreach (var button in buttons)
                        {
                            button.onClick.RemoveAllListeners();
                        }
                        
                        // Addressable 에셋 해제
                        AddressableController.Instance.ReleaseHandler(AddressKey.UI_LoginScene.ToString());
                        AddressableController.Instance.ReleaseHandler(AddressKey.UI_AlarmPopup.ToString());
                        
                        Utils.CreateLogMessage<SceneLoadController>($"UI_LoginScene 관련 에셋 해제 완료");
                    }
                }
                
                // 가비지 컬렉션 강제 실행
                System.GC.Collect();
                
                Utils.CreateLogMessage<SceneLoadController>($"씬 {_scene.name}의 Addressable 에셋 정리 완료");
            }
            catch (System.Exception e)
            {
                Utils.CreateLogMessage<SceneLoadController>($"씬 {_scene.name} 에셋 정리 중 오류: {e.Message}");
            }
            
            yield return new WaitForEndOfFrame();
        }
        #endregion
    }
}