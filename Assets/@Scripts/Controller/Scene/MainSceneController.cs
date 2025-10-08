using JJORY.Module;
using JJORY.Util;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace JJORY.Scene
{
    public class MainSceneController : MonoBehaviour
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Start()
        {
            Utils.CreateLogMessage<MainSceneController>("Main Scene Load Complete!");

            StartCoroutine(InstantiateAsset(AddressKey.UI_CharacterInfoPopup.ToString(), gameObject));
        }
        #endregion

        #region Method
        private IEnumerator InstantiateAsset(string _key, GameObject _parent)
        {
            AsyncOperationHandle handler;

            while (!AddressableController.Instance.GetHandler(_key, out handler))
            {
                yield return null;
            }

            // 로딩 완료될 때까지 대기
            while (!handler.IsDone)
            {
                yield return null;
            }

            GameObject prefab = handler.Result as GameObject;
            GameObject go = AddressableController.Instance.InstantiatePrefab(_key, prefab);
            go.transform.parent = _parent.transform;
        }
        #endregion
    }
}