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
                yield return null;
                SceneLoadController.Instance.LoadSceneByTags("main");
            }
        }

        #region LifeCycle
        private void Start()
        {
            AddressableLoad addressableLoad = new AddressableLoad();
            SceneModuleLoad sceneModuleLoad = new SceneModuleLoad();
            MoveScene moveScene = new MoveScene();

            SequenceActionUtils.Instance.Enqueue(addressableLoad);
            SequenceActionUtils.Instance.Enqueue(sceneModuleLoad);
            SequenceActionUtils.Instance.Enqueue(moveScene);

            SequenceActionUtils.Instance.DoSequenceAction();
        }
        #endregion
    }
}