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
                yield return null;
            }
        }

        class GameManage : Sequence
        {
            public IEnumerator Execute()
            {
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
            }
        }

        class MoveScene : Sequence
        {
            public IEnumerator Execute()
            {
                UIController.Instance.CloseMask();
                SceneLoadController.Instance.LoadSceneByTags("Login");
                yield return null;
            }
        }

        class EventControl : Sequence
        {
            public IEnumerator Execute()
            {
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
