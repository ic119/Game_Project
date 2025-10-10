using JJORY.Module;
using JJORY.Util;
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
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.UI_CharacterInfoPopup.ToString());
            AddressableController.Instance.LoadPrefab<GameObject>(AddressKey.StatusInfoItem.ToString());
        }

        private void Start()
        {
            StartCoroutine(InstantiateAsset(AddressKey.UI_LoginScene.ToString(), gameObject));
        }

        private void OnDestroy() 
        {
            Utils.CreateLogMessage<LoginSceneController>("LoginScene 제거");
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_LoginScene.ToString());
            AddressableController.Instance.ReleaseHandler(AddressKey.UI_AlarmPopup.ToString());
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