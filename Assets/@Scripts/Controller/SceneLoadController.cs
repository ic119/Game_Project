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
                //Utils.GenerateLogMessage<SceneLoadController>($"{tags}는 없는 태그 입니다.");
            }
            else
            {
                currentSceneModel = dicSceneModels[_tag];
                StartCoroutine(SceneLoadRoutine(currentSceneModel));
            }
        }

        private IEnumerator SceneLoadRoutine(SceneModel _targetModel)
        {
            UIController.Instance.CloseMask();
            yield return new WaitForSeconds(1.0f);
            cur_LoadProgress = 0.0f;

            AsyncOperation asyncLoadingScene = SceneManager.LoadSceneAsync(DEFINE.LOADING_SCENE);
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
        #endregion
    }
}