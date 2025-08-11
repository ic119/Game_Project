using JJORY.Model;
using JJORY.Model.SO;
using JJORY.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;

namespace JJORY.Module
{
    public class SceneLoadController : SingletonObject<SceneLoadController>
    {
        #region Variable
        [SerializeField] private SceneModel cur_SceneModel;
        private Dictionary<string, SceneModel> sceneModel_Dictionary = new Dictionary<string, SceneModel>();

        [Range(0.0f, 2.0f)]
        [SerializeField] private float delayTime;
        [SerializeField] private string scene_Address = "Load_Scene";

        public float cur_LoadProgress { get; private set; } = 0.0f;
        public bool isLoading { get; private set; } = false;
        #endregion

        #region Method
        public void Init(Action _onReady)
        {
            var handle = Addressables.LoadAssetAsync<SceneModelSO>("SceneModelSO");
            handle.Completed += h =>
            {
                if (h.Status != AsyncOperationStatus.Succeeded || h.Result == null)
                {
                    Debug.LogError("[SceneLoadController] SceneModelSO 로드 실패");
                    _onReady?.Invoke();
                    return;
                }

                sceneModel_Dictionary = new Dictionary<string, SceneModel>();
                foreach (var model in h.Result.scneneModel_List)
                {
                    if (!sceneModel_Dictionary.ContainsKey(model.sceneTag))
                        sceneModel_Dictionary.Add(model.sceneTag, model);
                }

                _onReady?.Invoke();
            };
        }

        private void LoadSceneByTags(string _tag)
        {
            if(sceneModel_Dictionary != null)
            {
                Utils.CreateLogMessage<SceneLoadController>("Init 상태 미완료");
                return;
            }

            if (sceneModel_Dictionary.TryGetValue(_tag, out var model) == false)
            {
                Utils.CreateLogMessage<SceneLoadController>("존재 하지 않는 Tag");
                return;
            }

            if (isLoading == true)
            {
                Utils.CreateLogMessage<SceneLoadController>("로딩 중");
                return;
            }

            cur_SceneModel = model;
            
        }

        private IEnumerator LoadAddressableAsset(SceneModel _targetModel)
        {
            isLoading = true;
            cur_LoadProgress = 0.0f;


            #region Loading Scene 추가
            var loading_Handle = Addressables.LoadSceneAsync(scene_Address, LoadSceneMode.Additive);

            while(!loading_Handle.IsDone)
            {
                cur_LoadProgress = Mathf.Clamp01(loading_Handle.PercentComplete * 0.1f);
                yield return null;
            }

            if (loading_Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Utils.CreateLogMessage<SceneLoadController>("Load_Scene 로드 실패");
                isLoading = false;
                yield break;
            }
            #endregion
            var scenes = _targetModel.loadScenes;
            int total = Mathf.Max(1, scenes.Count);

            for (int i = 0; i < scenes.Count; i++)
            {
                var scene_Key = scenes[i];
                var scene_Handler = Addressables.LoadSceneAsync(scene_Key, LoadSceneMode.Additive);

                while(!scene_Handler.IsDone)
                {
                    float base_Progress = 0.1f + (0.9f * (i / (float)total));
                    cur_LoadProgress = Mathf.Clamp01(base_Progress + (0.9f / total) * scene_Handler.PercentComplete);
                    yield return null;  
                }

                if (scene_Handler.Status != AsyncOperationStatus.Succeeded)
                {
                    Utils.CreateLogMessage<SceneLoadController>($"{scene_Key} 로드 실패");
                    break;
                }

                if (_targetModel.activeScene == scene_Key)
                {
                    var loaded = scene_Handler.Result.Scene;
                    SceneManager.SetActiveScene(loaded);
                }

                if (delayTime > 0.0f)
                {
                    yield return new WaitForSeconds(delayTime);
                }
            }

            var unLoad_Handler = Addressables.UnloadSceneAsync(loading_Handle);
            while(!unLoad_Handler.IsDone)
            {
                yield return null;
            }

            cur_LoadProgress = 1.0f;
            isLoading = false;
        }
        #endregion
    }
}