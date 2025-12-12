using JJORY.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

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
                Utils.CreateLogMessage<AddressableController>($"키[{_key}]를 key_HashSet 추가 완료!");
            }
        }

        public void RemoveKeyFromHashSet(string _key)
        {
            if (key_HashSet.Count > 0)
            {
                if (key_HashSet.Contains(_key))
                {
                    key_HashSet.Remove(_key);
                    Utils.CreateLogMessage<AddressableController>($"키[{_key}]를 key_HashSet 제거 완료!");
                }
                else
                {
                    Utils.CreateLogMessage<AddressableController>($"키[{_key}] Key_HashSet에 존재하지 않음");
                }
            }
        }

        public void AllRemoveKeyHashSet()
        {
            if (key_HashSet != null && key_HashSet.Count > 0)
            {
                key_HashSet.Clear();
                Utils.CreateLogMessage<AddressableController>("Key_HashSet 전체 제거 완료");
            }
        }

        /// <summary>
        /// address값을 통하여 Asset Load 처리
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_address">Key 값</param>
        /// <param name="_onLoad">콜백함수</param>
        public void LoadPrefab<T>(string _address, Action<T> _onLoad = null) where T : UnityEngine.Object
        {
            if (GetHandler(_address, out var _handler))
            {
                return;
            }
            else
            {
                AsyncOperationHandle<T> handler = Addressables.LoadAssetAsync<T>(_address);
                Debug.Log($">>>>> {_address} 로드 성공");

                handler.Completed += h =>
                {
                    if (h.Status == AsyncOperationStatus.Succeeded)
                    {
                        if (key_Dictionary.ContainsKey(_address) == false)
                        {
                            key_Dictionary.Add(_address, h);
                        }
                        else
                        {
                            Utils.CreateLogMessage<AddressableController>($"이미 {_address} 키가 존재합니다.");
                            Addressables.Release(h);
                        }
                        _onLoad?.Invoke(h.Result);

                        Utils.CreateLogMessage<AddressableController>($"Current Key_Dictionary Count : {key_Dictionary.Count}");
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
                Utils.CreateLogMessage<AddressableController>($"Get Type : {typeof(T)}");
                return GameObject.Instantiate(_go) as T;
            }

            Utils.CreateLogMessage<AddressableController>($"Get Type : {typeof(T)}");
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
                Utils.CreateLogMessage<AddressableController>($"{_key}는 아직 로드되지 않았습니다.");
                return null;
            }

            if (_handler.IsValid() == false || _handler.Status != AsyncOperationStatus.Succeeded)
            {
                Utils.CreateLogMessage<AddressableController>($"{_key}의 Handler가 유효하지 않거나 로드 실패했습니다.");
                return null;
            }

            var asset = _handler.Result as T;

            if (asset == null)
            {
                Utils.CreateLogMessage<AddressableController>($"[{_key}] 에셋을 {typeof(T).Name}로 변환할 수 없습니다. 실제 타입: {_handler.Result?.GetType().Name}");
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

        /// <summary>
        /// Load Object에서 Key값으로 해당 오브젝트 생성_Coroutine 함수용
        /// </summary>
        /// <param name="_key"></param>
        /// <param name="_parent"></param>
        /// <returns></returns>
        public IEnumerator InstantiateAsset(string _key, GameObject _parent)
        {
            AsyncOperationHandle handler;

            while (!GetHandler(_key, out handler))
            {
                yield return null;
            }

            // 로딩 완료될 때까지 대기
            while (!handler.IsDone)
            {
                yield return null;
            }

            GameObject prefab = handler.Result as GameObject;
            GameObject go = InstantiatePrefab(prefab);
            go.transform.SetParent(_parent.transform, false);
        }
        #endregion
    }
}