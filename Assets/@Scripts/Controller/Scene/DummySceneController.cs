using JJORY.Module;
using JJORY.Util;
using System.Collections;
using UnityEngine;

namespace JJORY.Scene.Dummy
{
    public class DummySceneController : MonoBehaviour
    {
        class AddressableLoad : Sequence
        {
            public IEnumerator Execute()
            {
                Utils.CreateLogMessage<DummySceneController>("1. AddressableLoad 모듈 로드 성공");    
                yield return null;
            }
        }

        class GameManage : Sequence
        {
            public IEnumerator Execute()
            {
                Utils.CreateLogMessage<DummySceneController>("2. GameManager 모듈 로드 성공");
                yield return null;
            }
        }

        class SceneModuleLoad : Sequence
        {
            public IEnumerator Execute()
            {
                bool isFlag = false;
                SceneLoadController.Instance.Init(() =>
                {
                    isFlag = true;
                });

                while(!isFlag)
                {
                    yield return null;
                }
                Utils.CreateLogMessage<DummySceneController>("3. SceneModule 모듈 로드 성공");
            }
        }

        class MoveScene : Sequence
        {
            public IEnumerator Execute()
            {
                UIController.Instance.CloseMask();
                Utils.CreateLogMessage<DummySceneController>("4. Scene 로드 성공");
                SceneLoadController.Instance.LoadSceneByTags("Login");
                yield return null;
            }
        }

        class EventControl : Sequence
        {
            public IEnumerator Execute()
            {
                Utils.CreateLogMessage<DummySceneController>("5. EventController 모듈 로드 성공");
                yield return null;
            }
        }

        #region LifeCycle
        private void Start()
        {
            AddressableLoad addressableLoad = new AddressableLoad();
            GameManage gameManage = new GameManage();
            SceneModuleLoad sceneModuleLoad = new SceneModuleLoad();
            MoveScene moveScene = new MoveScene();
            EventControl eventControl = new EventControl();

            SequenceActionUtils.Instance.Enqueue(addressableLoad);
            SequenceActionUtils.Instance.Enqueue(gameManage);
            SequenceActionUtils.Instance.Enqueue(sceneModuleLoad);
            SequenceActionUtils.Instance.Enqueue(moveScene);
            SequenceActionUtils.Instance.Enqueue(eventControl);

            SequenceActionUtils.Instance.DoSequenceAction();
        }
        #endregion
    }
}
