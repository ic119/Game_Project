using JJORY.Module;
using System.Collections;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace JJORY.Scene.Login
{
      
    public class LoginSceneController : MonoBehaviour
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Awake()
        {
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.UI_LoginScene.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.UI_AlarmPopup.ToString());

        }

        private void Start()
        {
            StartCoroutine(InstantiateAsset(AddressKey.UI_LoginScene.ToString(), gameObject));
        }
        #endregion

        #region Method
        /// <summary>
        /// Addressable Asset 생성 처리 메서드
        /// </summary>
        /// <param name="_key">Addressable 등록 key</param>
        /// <param name="_parent">위치 선정 부모 오브젝트</param>
        /// <returns></returns>
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