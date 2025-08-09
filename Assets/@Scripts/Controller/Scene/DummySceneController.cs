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
                Utils.CreateLogError<DummySceneController>("1. AddressableLoad module");    
                yield return null;
            }
        }
    }
}