using JJORY.Util;
using System;
using System.Collections;
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
        public HashSet<string> key_HashSet = new HashSet<string>();
        #endregion

        #region Method
        /// <summary>
        /// Addressable Asset에서 Load하기 위한 Key 값을 중복 방지를 위해 HashSet에 추가 처리
        /// </summary>
        /// <param name="_key"></param>
        public void AddKeyHashSet(string _key)
        {
            if (key_HashSet != null)
            {
                key_HashSet.Add(_key);
            }
        }

        public void RemoveKeyFromHashSet(string _key)
        {
            if (key_HashSet.Count > 0)
            {
                if (key_HashSet.Contains(_key))
                {
                    key_HashSet.Remove(_key);
                }
            }
        }

        public void AllRemoveKeyHashSet()
        {
            if (key_HashSet != null && key_HashSet.Count > 0)
            {
                key_HashSet.Clear();
            }
        }

        public void LoadPrefabAddressFromHashSet(Action<GameObject> _onLoad = null)
        {
            if (key_HashSet.Count > 0)
            {
                foreach(var key in key_HashSet)
                {
                    LoadPrefabAddress<GameObject>(key, _onLoad);
                }
            }
        }

        /// <summary>
        /// address값을 통하여 Asset Load 처리
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_address">Key 값</param>
        /// <param name="_onLoad">콜백함수</param>
        public void LoadPrefabAddress<T>(string _key, Action<T> _onLoad = null) where T : UnityEngine.Object
        {
            if (GetHandler(_key, out var _handler))
            {
                return;
            }
            else
            {
                AsyncOperationHandle<T> handler = Addressables.LoadAssetAsync<T>(_key);

                handler.Completed += h =>
                {
                    if (h.Status == AsyncOperationStatus.Succeeded)
                    {
                        if (!key_Dictionary.ContainsKey(_key))
                        {
                            key_Dictionary.Add(_key, h);
                        }
                        _onLoad?.Invoke(h.Result);

                    }
                    else
                    {
                        Addressables.Release(h);
                    }
                };
            }
        }

        /// <summary>
        /// Address없이 참조된 prefab 자체를 GameObject형태로 Instantiate 처리
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_address">key 값</param>
        /// <param name="_type">제너릭 타입</param>
        /// <returns></returns>
        public T InstantiatePrefab<T>(T _type) where T : UnityEngine.Object
        {
            if (_type is GameObject _go)
            {
                return GameObject.Instantiate(_go) as T;
            }
            return _type;
        }

        /// <summary>
        /// Address를 통해 Handler Check 후 해당 GameObject를 Instantiate 진행 후 위치 선정
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_key"></param>
        /// <param name="_parent"></param>
        /// <returns></returns>
        public T InstantiatePrefabHelper<T>(string _key, Transform _parent = null) where T : UnityEngine.Object
        {
            if (GetHandler(_key, out var _handler) == false)
            {
                return null;
            }

            if (_handler.IsValid() == false || _handler.Status != AsyncOperationStatus.Succeeded)
            {
                return null;
            }

            var asset = _handler.Result as T;

            if (asset == null)
            {
                return null;
            }

            if (asset is GameObject prefab)
            {
                if (_parent == null)
                {
                    return UnityEngine.Object.Instantiate(prefab) as T;
                }
                GameObject go = InstantiatePrefab(prefab);
                go.transform.SetParent(_parent.transform, false);
                return go as T;
            }

            return asset;
        }

        /// <summary>
        /// Dictionary에 Add 되어진 key값으로 통해 handler 추출
        /// </summary>
        /// <param name="_address"></param>
        /// <param name="_handler"></param>
        /// <returns></returns>
        public bool GetHandler(string _address, out AsyncOperationHandle _handler)
        {
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

        /// <summary>
        /// Load Object에서 Key값으로 해당 오브젝트 생성_Coroutine 함수용
        /// (부모 객체가 필요 없을 경우 오버로드 사용)
        /// </summary>
        /// <param name="_key"></param>
        /// <returns></returns>
        public IEnumerator InstantiateAsset(string _key)
        {
            yield return InstantiateAsset(_key, null, null);
        }

        /// <summary>
        /// Load Object에서 Key값으로 해당 오브젝트 생성_Coroutine 함수용
        /// </summary>
        /// <param name="_key"></param>
        /// <param name="_parent"></param>
        /// <returns></returns>
        public IEnumerator InstantiateAsset(string _key, GameObject _parent)
        {
            yield return InstantiateAsset(_key, _parent, null);
        }

        /// <summary>
        /// Load Object에서 Key값으로 해당 오브젝트 생성 및 콜백 반환_Coroutine 함수용
        /// </summary>
        /// <param name="_key"></param>
        /// <param name="_parent"></param>
        /// <param name="_onInstantiated"></param>
        /// <returns></returns>
        public IEnumerator InstantiateAsset(string _key, GameObject _parent, Action<GameObject> _onInstantiated)
        {
            Transform parentTr = _parent != null ? _parent.transform : null;
            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(_key, parentTr, false);

            yield return handle;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                GameObject go = handle.Result;
                
                int index = _key.LastIndexOf('/');
                string cleanName = index >= 0 ? _key.Substring(index + 1) : _key;
                go.name = cleanName;

                _onInstantiated?.Invoke(go);
            }
            else
            {
                Debug.LogError($"Failed to instantiate addressable asset with key: {_key}");
                _onInstantiated?.Invoke(null);
            }
        }
        #endregion
    }
}
