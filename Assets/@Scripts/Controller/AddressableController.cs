using JJORY.Util;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace JJORY.Module
{
    public class AddressableController : SingletonObject<AddressableController>
    {
        #region Variable
        [Header("Handler 관련")]
        public Dictionary<string, AsyncOperationHandle> key_Dictionary = new Dictionary<string, AsyncOperationHandle>();
        #endregion

        #region Method
        /// <summary>
        /// address값을 통하여 Asset Load 처리
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_address">Key 값</param>
        /// <param name="_onLoad">콜백함수</param>
        public void LoadPrefab<T>(string _address, Action<T> _onLoad = null) where T : UnityEngine.Object
        {
            AsyncOperationHandle<T> handler = Addressables.LoadAssetAsync<T>(_address);
            Debug.Log($">>>>> {_address} Loaded");

            handler.Completed += h =>
            {
                if (h.Status == AsyncOperationStatus.Succeeded)
                {
                    key_Dictionary.Add(_address, h);
                    Debug.Log($">>>>> key_Dictionary Count : {key_Dictionary.Count}");
                    _onLoad?.Invoke(h.Result);
                }
                else
                {
                    Addressables.Release(h);
                }
            };
        }

        /// <summary>
        /// Address를 통해 Load되어진 Asset을 Instantiate
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_address">key 값</param>
        /// <param name="_type">제너릭 타입</param>
        /// <returns></returns>
        public T InstantiatePrefab<T>(string _address, T _type) where T : UnityEngine.Object
        {
            if (_type is GameObject _go)
            {
                Debug.Log($">>>>> Get Type : {typeof(T)}");
                return GameObject.Instantiate(_go) as T;
            }

            Debug.Log($">>>>> Get Type : {typeof(T)}");
            return _type;
        }

        /// <summary>
        /// Dictionary에 Add 되어진 key값으로 통해 handler 추출
        /// </summary>
        /// <param name="_address"></param>
        /// <param name="_handler"></param>
        /// <returns></returns>
        public bool GetHandler(string _address, out AsyncOperationHandle _handler)
        {
            Debug.Log($">>>>> Get {_address}");
            return key_Dictionary.TryGetValue(_address, out _handler);
        }

        /// <summary>
        /// 사용하지않는 Handler 해제 처리
        /// </summary>
        /// <param name="_key"></param>
        public void ReleaseHandler(string _key)
        {
            if (key_Dictionary.TryGetValue(_key, out var _handler))
            {
                Addressables.Release(_handler);
                key_Dictionary.Remove(_key);
            }
        }
        #endregion

    }
}