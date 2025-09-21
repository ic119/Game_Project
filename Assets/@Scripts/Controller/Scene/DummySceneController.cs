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
                Utils.CreateLogMessage<DummySceneController>("1. AddressableLoad module Load Complete");    
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
                Utils.CreateLogMessage<DummySceneController>("2. SceneModule Load Complete");
            }
        }

        class MoveScene : Sequence
        {
            public IEnumerator Execute()
            {
                SceneLoadController.Instance.LoadSceneByTags("Login");
                yield return null;
            }
        }

        class EventControl : Sequence
        {
            public IEnumerator Execute()
            {
                Utils.CreateLogMessage<DummySceneController>("3. EventController Load Complete");
                yield return null;
            }
        }

        #region LifeCycle
        private void Start()
        {
            AddressableLoad addressableLoad = new AddressableLoad();
            SceneModuleLoad sceneModuleLoad = new SceneModuleLoad();
            MoveScene moveScene = new MoveScene();
            EventControl eventControl = new EventControl();

            SequenceActionUtils.Instance.Enqueue(addressableLoad);
            SequenceActionUtils.Instance.Enqueue(sceneModuleLoad);
            SequenceActionUtils.Instance.Enqueue(moveScene);
            SequenceActionUtils.Instance.Enqueue(eventControl);


            SequenceActionUtils.Instance.DoSequenceAction();
        }
        #endregion
    }
}