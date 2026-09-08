using Incheol.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Modules
{
    public class AddressableAssetManager : SingletonObject<AddressableAssetManager>
    {
        /// <summary>
        /// keyDictionary 등 로드 캐시는 씬 전환을 넘어 유지되어야 하므로, 이 매니저 자신도 파괴되지 않아야 한다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

        #region Variable
        private readonly Dictionary<string, AsyncOperationHandle> keyDictionary = new Dictionary<string, AsyncOperationHandle>();
        private readonly HashSet<string> keyHashSet = new HashSet<string>();
        private readonly HashSet<string> loadingKeyHashSet = new HashSet<string>();
        private readonly HashSet<string> failedKeyHashSet = new HashSet<string>();

        private readonly Dictionary<string, AsyncOperationHandle> loadingHandleDictionary = new Dictionary<string, AsyncOperationHandle>();
        private readonly Dictionary<string, List<Action<UnityEngine.Object>>> loadingCallbackDictionary = new Dictionary<string, List<Action<UnityEngine.Object>>>();

        #endregion

        #region Method
        public void Init()
        {
            ReleaseAllHandler();
        }

        public void AddKeyHashSet(string _key)
        {
            keyHashSet.Add(_key);
        }

        public void DeleteKeyHashSet(string _key)
        {
            keyHashSet.Remove(_key);
        }

        public void LoadPrefabAddressFromHashSet(Action<string, GameObject> _onLoad = null)
        {
            if (keyHashSet.Count > 0)
            {
                foreach (var key in keyHashSet)
                {
                    LoadPrefabAddress<GameObject>(key, result => _onLoad?.Invoke(key, result));
                }
            }
        }

        public void LoadPrefabAddress<T>(string _key, Action<T> _onLoad = null) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(_key))
            {
                return;
            }

            if (GetHandler(_key, out var _handler))
            {
                if (_handler.Status == AsyncOperationStatus.Succeeded)
                {
                    if (_handler.Result is T result)
                    {
                        _onLoad?.Invoke(result);
                    }
                    else
                    {
                        DebugLogManager.GenerateErrorMessage<AddressableAssetManager>($"Addressable 캐시 타입 불일치 Key : {_key}, 캐시된 타입 : {_handler.Result?.GetType().Name}, 요청한 타입 : {typeof(T).Name}");
                    }
                }
                return;
            }

            if (loadingHandleDictionary.ContainsKey(_key))
            {
                RegisterLoadingCallback(_key, _onLoad);
                return;
            }

            // 재시도 시 이전 실패 기록 제거
            failedKeyHashSet.Remove(_key);
            loadingKeyHashSet.Add(_key);
            AsyncOperationHandle<T> handler;

            try
            {
                handler = Addressables.LoadAssetAsync<T>(_key);
            }
            catch (Exception exception)
            {
                loadingKeyHashSet.Remove(_key);
                failedKeyHashSet.Add(_key);
                DebugLogManager.GenerateErrorMessage<AddressableAssetManager>($"Addressable 로드 실패(잘못된 Key) Key : {_key}, Exception : {exception}");
                return;
            }

            loadingHandleDictionary[_key] = handler;
            RegisterLoadingCallback(_key, _onLoad);

            handler.Completed += h =>
            {
                bool _isCurrent = loadingHandleDictionary.TryGetValue(_key, out var _currentHandle) && _currentHandle.Equals(handler);

                List<Action<UnityEngine.Object>> _callbacks = null;

                if (_isCurrent)
                {
                    loadingKeyHashSet.Remove(_key);
                    loadingHandleDictionary.Remove(_key);
                    loadingCallbackDictionary.TryGetValue(_key, out _callbacks);
                    loadingCallbackDictionary.Remove(_key);
                }

                if (!_isCurrent)
                {
                    if (h.Status == AsyncOperationStatus.Succeeded)
                    {
                        Addressables.Release(h);
                    }
                    return;
                }

                if (h.Status == AsyncOperationStatus.Succeeded)
                {
                    if (!keyDictionary.ContainsKey(_key))
                    {
                        keyDictionary.Add(_key, h);
                    }

                    if (_callbacks != null)
                    {
                        foreach (var _callback in _callbacks)
                        {
                            // 콜백 하나가 예외를 던지더라도 같은 Key를 기다리던 다른 호출자의 콜백까지
                            // 함께 유실되지 않도록 각 콜백 호출을 개별적으로 격리한다.
                            try
                            {
                                _callback(h.Result);
                            }
                            catch (Exception exception)
                            {
                                DebugLogManager.GenerateErrorMessage<AddressableAssetManager>($"Addressable 로드 완료 콜백 처리 중 예외 발생 Key : {_key}, Exception : {exception}");
                            }
                        }
                    }
                }
                else
                {
                    failedKeyHashSet.Add(_key);
                    DebugLogManager.GenerateErrorMessage<AddressableAssetManager>($"Addressable 로드 실패 Key : {_key}, Status : {h.Status}, Exception : {h.OperationException}");
                }
            };
        }

        private void RegisterLoadingCallback<T>(string _key, Action<T> _onLoad) where T : UnityEngine.Object
        {
            if (_onLoad == null)
            {
                return;
            }

            if (!loadingCallbackDictionary.TryGetValue(_key, out var _callbackList))
            {
                _callbackList = new List<Action<UnityEngine.Object>>();
                loadingCallbackDictionary[_key] = _callbackList;
            }

            _callbackList.Add(_result =>
            {
                if (_result is T typedResult)
                {
                    _onLoad.Invoke(typedResult);
                }
                else
                {
                    DebugLogManager.GenerateErrorMessage<AddressableAssetManager>($"Addressable 로딩 중인 Key를 다른 타입으로 재요청했습니다 Key : {_key}, 요청한 타입 : {typeof(T).Name}");
                }
            });
        }

        public bool IsLoading(string _key)
        {
            return !string.IsNullOrEmpty(_key) && loadingKeyHashSet.Contains(_key);
        }

        public bool HasLoadFailed(string _key)
        {
            return !string.IsNullOrEmpty(_key) && failedKeyHashSet.Contains(_key);
        }

        public bool IsLoaded(string _key)
        {
            return GetHandler(_key, out _);
        }

        public async Awaitable WaitForLoadAsync(string _key, Func<bool> _isCancelled = null)
        {
            while (true)
            {
                if (_isCancelled != null && _isCancelled())
                {
                    return;
                }

                if (GetHandler(_key, out _))
                {
                    return;
                }

                if (HasLoadFailed(_key))
                {
                    return;
                }

                await Awaitable.NextFrameAsync();
            }
        }

        public T InstantiatePrefab<T>(T _type, Transform _parent = null) where T : UnityEngine.Object
        {
            if (_type is GameObject go)
            {
                return GameObject.Instantiate(go, _parent) as T;
            }
            return _type;
        }

        public bool GetHandler(string _key, out AsyncOperationHandle _handler)
        {
            return keyDictionary.TryGetValue(_key, out _handler);
        }

        public void ReleaseHandler(string _key)
        {
            loadingKeyHashSet.Remove(_key);
            failedKeyHashSet.Remove(_key);

            if (GetHandler(_key, out var _handler))
            {
                Addressables.Release(_handler);
                keyDictionary.Remove(_key);
                DeleteKeyHashSet(_key);
                return;
            }

            if (loadingHandleDictionary.Remove(_key))
            {
                loadingCallbackDictionary.Remove(_key);
                DeleteKeyHashSet(_key);
                return;
            }

            keyHashSet.Remove(_key);
        }

        public void ReleaseAllHandler(ICollection<string> _preserveKeys = null)
        {
            List<string> keysToRelease = new List<string>();
            foreach (string key in keyDictionary.Keys)
            {
                if (_preserveKeys == null || !_preserveKeys.Contains(key))
                {
                    keysToRelease.Add(key);
                }
            }

            for (int i = 0; i < keysToRelease.Count; i++)
            {
                string key = keysToRelease[i];
                Addressables.Release(keyDictionary[key]);
                keyDictionary.Remove(key);
                keyHashSet.Remove(key);
            }

            // 로딩 중이던 핸들은 완료 전에는 안전하게 Release할 수 없으므로 추적만 제거한다.
            // 실제 Addressables.Release는 LoadPrefabAddress의 Completed 콜백이 완료 시점에 대신 처리한다.
            loadingHandleDictionary.Clear();
            loadingCallbackDictionary.Clear();

            loadingKeyHashSet.Clear();
            failedKeyHashSet.Clear();
        }
        #endregion
    }
}
