using Incheol.Util;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Incheol.Module
{
    public class LoadSceneController : SingletonObject<LoadSceneController>
    {
        #region Variable
        #endregion

        #region LifeCycle
        #endregion

        #region Method
        /// <summary>
        /// 지정한 이름의 Unity Scene을 비동기로 Load한다.
        /// </summary>
        public async Awaitable LoadSceneAsync(string _sceneName, LoadSceneMode _mode = LoadSceneMode.Single)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(_sceneName, _mode);

            if (operation == null)
            {
                Utils.CreateLogError<LoadSceneController>($"LoadSceneAsync 실패: {_sceneName}");
                return;
            }

            while (!operation.isDone)
            {
                await Awaitable.NextFrameAsync();
            }
        }
        #endregion
    }
}
