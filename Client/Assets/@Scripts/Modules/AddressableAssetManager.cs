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
        #region Variable
        private readonly Dictionary<string, AsyncOperationHandle> keyDictionary = new Dictionary<string, AsyncOperationHandle>();
        private readonly HashSet<string> keyHashSet = new HashSet<string>();
        private readonly HashSet<string> loadingKeyHashSet = new HashSet<string>();
        private readonly HashSet<string> failedKeyHashSet = new HashSet<string>();


        /// <summary>
        /// 아직 완료되지 않은(로딩 중인) 핸들을 Key별로 추적한다. 완료되면 keyDictionary로 옮겨가고 여기서는 제거된다.
        /// - 같은 Key가 로딩 중일 때 재요청이 들어오면 새로 로드하지 않고 loadingCallbackDictionary에 콜백만 추가로 등록한다.
        /// - ReleaseHandler/ReleaseAllHandler가 로딩 도중 호출되면 아직 완료 전이라 즉시 Release할 수 없으므로,
        ///   여기서 추적만 지워두고 실제 Release는 Completed 콜백이 완료 시점에 대신 수행한다.
        /// - 완료 시점에는 "이 핸들이 여전히 loadingHandleDictionary에 등록된 그 핸들인지"를 비교해서,
        ///   Release된 뒤 같은 Key로 재요청이 들어와 다른 핸들로 덮어써진 경우(superseded)를 구분한다.
        /// </summary>
        private readonly Dictionary<string, AsyncOperationHandle> loadingHandleDictionary = new Dictionary<string, AsyncOperationHandle>();

        /// <summary>
        /// 로딩 중에 들어온 대기 콜백들을 Key별로 모아둔다. Completed는 항상 최초 로드 요청 하나에만 걸고,
        /// 이후 중복 요청자는 여기에만 콜백을 추가한다(핸들에 직접 Completed를 거는 대신).
        /// 이렇게 하면 로딩 도중 ReleaseHandler로 해제되었을 때 모든 대기자가 함께 취소되고,
        /// 이미 Release된 결과를 일부 대기자가 뒤늦게 넘겨받는 문제를 막을 수 있다.
        /// </summary>
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
                // 이미 같은 Key가 로딩 중이면 새로 로드하지 않고, 최초 로드의 Completed에서 함께 호출될
                // 대기 콜백 목록에만 등록한다. 핸들에 직접 Completed를 추가하지 않으므로,
                // 도중에 ReleaseHandler로 해제되면 이 콜백도 최초 요청자와 동일하게 호출되지 않는다.
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
                // 이 핸들이 여전히 loadingHandleDictionary에 등록된 "현재" 핸들인지 확인한다.
                // 다르다면(=현재 없거나 다른 핸들로 덮어써졌다면) 두 가지 경우 중 하나다.
                // 1) 로딩 도중 ReleaseHandler/ReleaseAllHandler로 해제됨
                // 2) 해제 이후 같은 Key로 새 로드가 다시 시작되어 이 핸들은 더 이상 최신 요청이 아님(superseded)
                // 어느 쪽이든 이 핸들 자신의 결과만 안전하게 Release하고, 최신 상태(다른 핸들의 추적)는 건드리지 않는다.
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

        /// <summary>
        /// _key의 Addressable 로드가 끝날 때까지(성공 또는 실패) 매 프레임 대기한다.
        /// 호출측은 매 반복마다 _isCancelled()로 자체 취소 조건(예: generationId 불일치)을 확인해
        /// 조기에 빠져나옴 수 있다. 여러 컨트롤러에서 각자 복사해 쓰던 "핸들러 폴링 + generationId 체크 + NextFrameAsync 루프" 패턴을 공통화한다.
        /// 대기가 끝난 뒤 실제 결과(성공/실패/취소)는 호출측이 GetHandler/HasLoadFailed로 직접 판단해야 한다.
        /// </summary>
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


        /// <summary>
        /// GameObject는 Instantiate로 복제해서 반환하지만, 그 외 타입(Texture, ScriptableObject 등)은
        /// Addressables에 캐시된 원본 참조를 그대로 반환한다. 호출자가 반환값을 변경하면 같은 Key를
        /// 캐시로 공유하는 다른 호출자에게도 영향을 주므로, GameObject가 아닌 타입은 읽기 전용으로만 사용해야 한다.
        /// </summary>
        public T InstantiatePrefab<T>(T _type) where T : UnityEngine.Object
        {
            if (_type is GameObject go)
            {
                return GameObject.Instantiate(go) as T;
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
                // 아직 로딩 중인 핸들은 완료 전에는 안전하게 Release할 수 없으므로 추적만 제거한다.
                // 실제 Addressables.Release는 LoadPrefabAddress의 Completed 콜백에서
                // (해당 핸들이 더 이상 loadingHandleDictionary의 "현재" 핸들이 아님을 감지한 뒤) 완료된 직후 수행된다.
                // 대기 중이던 콜백들도 함께 버려서, 해제된 결과가 뒤늦게 대기자에게 전달되지 않도록 한다.
                loadingCallbackDictionary.Remove(_key);
                DeleteKeyHashSet(_key);
                return;
            }

            keyHashSet.Remove(_key);
        }

        public void ReleaseAllHandler()
        {
            foreach (var kvp in keyDictionary)
            {
                Addressables.Release(kvp.Value);
            }

            // 로딩 중이던 핸들은 완료 전에는 안전하게 Release할 수 없으므로 추적만 제거한다.
            // 실제 Addressables.Release는 LoadPrefabAddress의 Completed 콜백이 완료 시점에 대신 처리한다.
            loadingHandleDictionary.Clear();
            loadingCallbackDictionary.Clear();

            keyDictionary.Clear();
            keyHashSet.Clear();
            loadingKeyHashSet.Clear();
            failedKeyHashSet.Clear();
        }
        #endregion
    }
}