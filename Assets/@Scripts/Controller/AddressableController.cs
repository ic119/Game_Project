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

        /// <summary>
        /// InstantiateAsync로 생성한 인스턴스 핸들. ReleaseInstance 시 사용한다.
        /// </summary>
        private readonly Dictionary<GameObject, AsyncOperationHandle<GameObject>> instanceHandle_Dictionary
            = new Dictionary<GameObject, AsyncOperationHandle<GameObject>>();
        #endregion

        #region Method
        /// <summary>
        /// Addressable Asset에서 Load하기 위한 Key 값을 중복 방지를 위해 HashSet에 추가 처리
        /// </summary>
        public void AddKeyHashSet(string _key)
        {
            if (key_HashSet != null)
            {
                key_HashSet.Add(_key);
            }
        }

        public void RemoveKeyFromHashSet(string _key)
        {
            if (key_HashSet.Count > 0 && key_HashSet.Contains(_key))
            {
                key_HashSet.Remove(_key);
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
            if (key_HashSet.Count <= 0)
            {
                return;
            }

            foreach (var key in key_HashSet)
            {
                LoadPrefabAddress<GameObject>(key, _onLoad);
            }
        }

        /// <summary>
        /// address값을 통하여 Asset Load 처리 (동기 InstantiatePrefabHelper용 프리로드)
        /// </summary>
        public void LoadPrefabAddress<T>(string _key, Action<T> _onLoad = null) where T : UnityEngine.Object
        {
            if (GetHandler(_key, out var _))
            {
                return;
            }

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

        /// <summary>
        /// Address없이 참조된 prefab 자체를 GameObject형태로 Instantiate 처리
        /// </summary>
        public T InstantiatePrefab<T>(T _type) where T : UnityEngine.Object
        {
            if (_type is GameObject _go)
            {
                return GameObject.Instantiate(_go) as T;
            }

            return _type;
        }

        /// <summary>
        /// 미리 LoadAssetAsync 된 핸들로 동기 Instantiate (알람 팝업 등 즉시 생성용)
        /// </summary>
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
                go.transform.SetParent(_parent, false);
                return go as T;
            }

            return asset;
        }

        public bool GetHandler(string _address, out AsyncOperationHandle _handler)
        {
            return key_Dictionary.TryGetValue(_address, out _handler);
        }

        public void ReleaseHandler(string _key)
        {
            if (key_Dictionary.TryGetValue(_key, out var _handler))
            {
                Addressables.Release(_handler);
                key_Dictionary.Remove(_key);
            }
        }

        /// <summary>
        /// Addressables.InstantiateAsync로 생성한 인스턴스를 해제한다.
        /// </summary>
        public void ReleaseInstance(GameObject _instance)
        {
            if (_instance == null)
            {
                return;
            }

            if (instanceHandle_Dictionary.TryGetValue(_instance, out var handle))
            {
                instanceHandle_Dictionary.Remove(_instance);
                Addressables.ReleaseInstance(handle);
            }
            else
            {
                Addressables.ReleaseInstance(_instance);
            }
        }

        public IEnumerator InstantiateAsset(string _key)
        {
            yield return InstantiateAsset(_key, null, null);
        }

        public IEnumerator InstantiateAsset(string _key, GameObject _parent)
        {
            yield return InstantiateAsset(_key, _parent, null);
        }

        /// <summary>
        /// Addressables.InstantiateAsync로 생성한다. 로드+생성을 Addressables가 처리해 프레임 분산에 유리하다.
        /// </summary>
        public IEnumerator InstantiateAsset(string _key, GameObject _parent, Action<GameObject> _onInstantiated)
        {
            Transform parentTr = _parent != null ? _parent.transform : null;
            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(_key, parentTr, false);

            // 진행률이 있을 때 프레임을 나눠 대기
            while (!handle.IsDone)
            {
                yield return null;
            }

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                GameObject go = handle.Result;
                go.name = GetCleanAddressName(_key);
                instanceHandle_Dictionary[go] = handle;
                _onInstantiated?.Invoke(go);
            }
            else
            {
                Utils.CreateLogError<AddressableController>($"InstantiateAsync 실패: {_key}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                _onInstantiated?.Invoke(null);
            }
        }

        private static string GetCleanAddressName(string _key)
        {
            if (string.IsNullOrEmpty(_key))
            {
                return _key;
            }

            int index = _key.LastIndexOf('/');
            return index >= 0 ? _key.Substring(index + 1) : _key;
        }
        #endregion
    }
}
