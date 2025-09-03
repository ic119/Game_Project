using JJORY.Module;
using System.Collections;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace JJORY.Scene
{
    public class LoginScene : MonoBehaviour
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Awake()
        {
            AddressableController.Instance.LoadPrefab<GameObject>("UI_LoginScene");
        }

        private void Start()
        {
            StartCoroutine(InstantiateAsset("UI_LoginScene"));
        }
        #endregion

        #region Method
        private IEnumerator InstantiateAsset(string _key)
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
            go.transform.parent = gameObject.transform;
        }
        #endregion
    }
}