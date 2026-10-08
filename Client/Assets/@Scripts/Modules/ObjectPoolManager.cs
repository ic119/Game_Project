using Incheol.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace Incheol.Modules
{
    /// <summary>
    /// Addressable 프리팹 오브젝트 풀. 이 클래스는 프리팹 로드(Preload/GetAsync), 대여/반환 흐름, IPoolable 훅 호출을 맡고,
    /// 인스턴스를 어디에 보관하고 언제 파괴하는지는 ObjectPoolStorage가 맡는다.
    /// </summary>
    public class ObjectPoolManager : SingletonObject<ObjectPoolManager>
    {
        /// <summary>
        /// 풀에 보관된 인스턴스는 씬 전환 후에도 계속 재사용해야 하므로, 이 매니저 자체가 씬 전환에도 파괴되지 않아야 한다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

        #region Variable
        private ObjectPoolStorage storage;

        // Awake/OnDestroy 순서와 무관하게 쓸 수 있도록 처음 접근할 때 만든다(필드 초기화에서는 this를 쓸 수 없다).
        private ObjectPoolStorage Storage => storage ??= new ObjectPoolStorage(this);
        #endregion

        #region LifeCycle
        protected override void Awake()
        {
            base.Awake();

            // 풀 루트는 직렬화되지 않아 Awake 시점엔 항상 비어 있는데, 씬에 저장돼 있던(또는 에디터에서 남은) 이전
            // "@ObjectPools" 자식이 있으면 GetOrCreateRoot가 새 루트를 또 만들어 루트가 둘이 된다. 더 나쁜 점은 옛 루트
            // 안의 인스턴스는 풀이 모르는 고아라서, 활성 상태면 화면에 그대로 남는다(텍스트 없는 알림 팝업 등).
            // 풀은 이 시점에 비어 있으므로 남아 있는 옛 루트는 모두 정리한다.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == ObjectPoolStorage.PoolRootName)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseAll(true);
        }
        #endregion

        #region Method
        /// <summary>
        /// 모든 풀과 인스턴스를 정리한다. (Addressable 핸들은 AddressableAssetController에서 별도 관리)
        /// </summary>
        public void Init()
        {
            ReleaseAll(true);
        }

        /// <summary>
        /// Addressable 프리팹을 로드 후 지정 개수만큼 미리 생성.
        /// 이미 로드되어 있어도 <paramref name="_onReady"/>는 항상 다음 프레임 이후에 호출된다.
        /// (캐시 여부에 따라 동기/비동기로 호출 시점이 달라지면 호출측이 실행 순서를 가정하기 어렵기 때문)
        /// </summary>
        /// <param name="_key">Addressable Key</param>
        /// <param name="_prewarmCount">미리 생성해 둘 인스턴스 수</param>
        /// <param name="_onReady">로드 및 프리워밍 완료 콜백</param>
        public void Preload(string _key, int _prewarmCount = 0, Action _onReady = null)
        {
            if (string.IsNullOrEmpty(_key))
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>("풀 키가 비어 있습니다.");
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>("AddressableAssetController.Instance가 없습니다.");
                return;
            }

            // 이미 로드된 경우 즉시 프리워밍하되, 콜백 호출은 로드 대기 경로와 시점을 맞추기 위해 한 프레임 미룬다.
            if (AddressableAssetManager.Instance.GetHandler(_key, out AsyncOperationHandle handle))
            {
                GameObject cachedPrefab = handle.Result as GameObject;
                Prewarm(_key, cachedPrefab, _prewarmCount);
                _ = InvokeNextFrameAsync(_onReady);
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(_key, prefab =>
            {
                // 로드가 끝나기 전에 이 컨트롤러(오브젝트)가 파괴되었을 수 있으므로(씬 전환, Play Mode 종료 등) 가드한다.
                if (this == null)
                {
                    return;
                }

                Prewarm(_key, prefab, _prewarmCount);
                _onReady?.Invoke();
            });
        }

        /// <summary>
        /// 프리팹이 이미 로드되어 있다는 가정 하에 동기적으로 인스턴스 대여.
        /// </summary>
        public GameObject Get(string _key, Transform _parent = null)
        {
            if (string.IsNullOrEmpty(_key))
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>("풀 키가 비어 있습니다.");
                return null;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>("AddressableAssetController.Instance가 없습니다.");
                return null;
            }

            GameObject go = Storage.Dequeue(_key);

            if (go == null)
            {
                GameObject prefab = GetLoadedPrefab(_key);
                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<ObjectPoolManager>($"'{_key}' 프리팹이 아직 로드되지 않았습니다. Preload 후 사용하거나 GetAsync를 사용하세요.");

                    // 후속 호출을 위해 로드만 미리 요청
                    AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(_key);
                    return null;
                }

                go = CreateInstance(_key, prefab);
            }

            if (go == null)
            {
                return null;
            }

            if (_parent != null)
            {
                go.transform.SetParent(_parent, false);
            }

            go.SetActive(true);
            Storage.Track(go, _key);

            // 재사용 시 커스텀 상태를 초기화할 수 있도록 IPoolable 훅을 호출한다.
            NotifyPoolable(go, poolable => poolable.OnGetFromPool());

            return go;
        }

        /// <summary>
        /// 위치/회전을 지정해 동기적으로 인스턴스를 대여한다.
        /// </summary>
        public GameObject Get(string _key, Vector3 _position, Quaternion _rotation, Transform _parent = null)
        {
            GameObject go = Get(_key, _parent);
            if (go != null)
            {
                go.transform.SetPositionAndRotation(_position, _rotation);
            }
            return go;
        }

        /// <summary>
        /// 프리팹이 로드되어 있지 않으면 Addressable 로드를 먼저 수행한 뒤 인스턴스를 대여한다.
        /// 결과는 <paramref name="_onSpawned"/> 콜백으로 전달된다.
        /// 이미 로드되어 있어도 콜백은 항상 다음 프레임 이후에 호출된다.
        /// (캐시 여부에 따라 동기/비동기로 호출 시점이 달라지면 호출측이 실행 순서를 가정하기 어렵기 때문)
        /// </summary>
        public void GetAsync(string _key, Action<GameObject> _onSpawned, Transform _parent = null)
        {
            if (string.IsNullOrEmpty(_key))
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>("풀 키가 비어 있습니다.");
                _onSpawned?.Invoke(null);
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>("AddressableAssetController.Instance가 없습니다.");
                _onSpawned?.Invoke(null);
                return;
            }

            // 이미 로드된 경우 즉시 대여하되, 콜백 호출은 로드 대기 경로와 시점을 맞추기 위해 한 프레임 미룬다.
            if (AddressableAssetManager.Instance.GetHandler(_key, out _))
            {
                GameObject spawned = Get(_key, _parent);
                _ = InvokeNextFrameAsync(() => _onSpawned?.Invoke(spawned));
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(_key, prefab =>
            {
                _onSpawned?.Invoke(Get(_key, _parent));
            });
        }

        /// <summary>
        /// 사용이 끝난 인스턴스를 원래 풀로 반환한다. (어떤 풀인지 자동 추적)
        /// </summary>
        /// <returns>풀에서 대여된 객체를 정상 반환했으면 true</returns>
        public bool Release(GameObject _go)
        {
            if (_go == null)
            {
                return false;
            }

            if (!Storage.TryUntrack(_go, out string key))
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>($"'{_go.name}'은(는) 풀에서 대여된 객체가 아닙니다.");
                return false;
            }

            // 반환 시점에 커스텀 정리를 할 수 있도록 비활성화 전에 IPoolable 훅을 먼저 호출한다.
            NotifyPoolable(_go, poolable => poolable.OnReleaseToPool());

            _go.SetActive(false);

            // Get() 후 CompensateParentScale 등으로 localScale이 변형된 채로 남아있을 수 있으므로,
            // 다음 대여 시 크기가 정확히 유지되도록 반환 시점에 원본 프리팹 스케일로 복원한다.
            GameObject prefab = GetLoadedPrefab(key);
            if (prefab != null)
            {
                _go.transform.localScale = prefab.transform.localScale;
            }

            _go.transform.SetParent(Storage.GetOrCreateRoot(key), false);

            // 최대 보관 개수가 설정되어 있고 이미 가득 찼다면, 큐에 쌓아두는 대신 즉시 파괴해
            // 대여/반환이 반복될 때 큐가 무한정 커지는 것을 막는다.
            if (!Storage.TryEnqueueReleased(key, _go))
            {
                Destroy(_go);
            }

            return true;
        }

        /// <summary>
        /// 특정 키의 풀을 비우고 제거한다.
        /// </summary>
        /// <param name="_destroyActive">true이면 대여 중(활성)인 인스턴스까지 파괴</param>
        /// <param name="_releaseAddressableHandle">
        /// true이면 AddressableAssetController에 남아있는 이 Key의 핸들도 함께 Release한다.
        /// 기본값 false는 기존 동작과 동일하게 핸들을 별도로 남겨둔다(호출측이 이후 재사용을 원할 수 있으므로).
        /// </param>
        public void ReleasePool(string _key, bool _destroyActive = false, bool _releaseAddressableHandle = false)
        {
            Storage.DestroyPool(_key, _destroyActive);

            if (_releaseAddressableHandle && AddressableAssetManager.Instance != null)
            {
                AddressableAssetManager.Instance.ReleaseHandler(_key);
            }
        }

        /// <summary>
        /// 모든 풀을 비우고 제거한다.
        /// </summary>
        /// <param name="_destroyActive">true이면 대여 중(활성)인 인스턴스까지 파괴</param>
        /// <param name="_releaseAddressableHandles">
        /// true이면 이 컨트롤러가 사용했던 모든 Key에 대해 AddressableAssetController에 남아있는 핸들도 함께 Release한다.
        /// 기본값 false는 기존 동작과 동일하게 핸들 관리를 AddressableAssetController 쪽에 맡긴다.
        /// </param>
        public void ReleaseAll(bool _destroyActive = false, bool _releaseAddressableHandles = false)
        {
            List<string> managedKeys = Storage.DestroyAll(_destroyActive, _releaseAddressableHandles);

            if (managedKeys != null && AddressableAssetManager.Instance != null)
            {
                for (int i = 0; i < managedKeys.Count; i++)
                {
                    AddressableAssetManager.Instance.ReleaseHandler(managedKeys[i]);
                }
            }
        }

        /// <summary>
        /// 키에 해당하는 풀이 등록되어 있는지 여부
        /// </summary>
        public bool HasPool(string _key)
        {
            return Storage.HasPool(_key);
        }

        /// <summary>
        /// 현재 이 풀이 추적 중인(대여 중이거나 풀에 보관 중인) 모든 Addressable Key.
        /// 씬 전환 시 Addressable 핸들을 일괄 정리하는 쪽에서, 여전히 사용 중인 프리팹은 보호하기 위해 사용한다.
        /// </summary>
        public HashSet<string> GetTrackedKeys()
        {
            return Storage.GetTrackedKeys();
        }

        /// <summary>
        /// 특정 키의 풀이 보관할 수 있는 최대 비활성 인스턴스 수를 설정한다.
        /// Release()로 반환되는 인스턴스가 이 개수를 초과하면 큐에 쌓아두지 않고 즉시 파괴해,
        /// 대여/반환이 반복될 때 큐가 무한정 커지는 것을 막는다.
        /// </summary>
        /// <param name="_maxSize">0 이하이면 무제한(설정 해제)</param>
        public void SetPoolCapacity(string _key, int _maxSize)
        {
            if (string.IsNullOrEmpty(_key))
            {
                return;
            }

            Storage.SetCapacity(_key, _maxSize);
        }

        private void Prewarm(string _key, GameObject _prefab, int _count)
        {
            if (this == null)
            {
                return;
            }

            if (_prefab == null)
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>($"'{_key}' 프리팹이 null이라 프리워밍할 수 없습니다.");
                return;
            }

            // 개수가 0이어도 빈 풀을 등록해 HasPool이 true가 되게 한다.
            Storage.EnsurePool(_key);

            for (int i = 0; i < _count; i++)
            {
                GameObject go = CreateInstance(_key, _prefab);
                if (go == null)
                {
                    return;
                }

                go.SetActive(false);
                Storage.Enqueue(_key, go);
            }
        }

        /// <summary>
        /// Preload/GetAsync가 이미 로드된 경우에도 대기 경로와 동일하게 다음 프레임 이후 콜백을 호출하도록
        /// 시점을 맞추기 위한 헬퍼. 예외가 나도 다른 처리에 영향을 주지 않도록 흡수하고 로그만 남긴다.
        /// </summary>
        private async Awaitable InvokeNextFrameAsync(Action _action)
        {
            if (_action == null)
            {
                return;
            }

            await Awaitable.NextFrameAsync();

            if (this == null)
            {
                return;
            }

            try
            {
                _action.Invoke();
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>($"지연 콜백 처리 중 예외 발생 : {exception}");
            }
        }

        private GameObject GetLoadedPrefab(string _key)
        {
            if (AddressableAssetManager.Instance == null)
            {
                return null;
            }

            if (AddressableAssetManager.Instance.GetHandler(_key, out AsyncOperationHandle handle))
            {
                return handle.Result as GameObject;
            }
            return null;
        }

        /// <summary>
        /// _go(및 자식)에 붙어있는 IPoolable 컴포넌트를 찾아 _invoke를 호출한다.
        /// 컴포넌트 하나가 예외를 던져도 나머지 컴포넌트/풀링 로직에 영향을 주지 않도록 개별적으로 격리한다.
        /// </summary>
        private static void NotifyPoolable(GameObject _go, Action<IPoolable> _invoke)
        {
            IPoolable[] poolables = _go.GetComponents<IPoolable>();
            for (int i = 0; i < poolables.Length; i++)
            {
                try
                {
                    _invoke(poolables[i]);
                }
                catch (Exception exception)
                {
                    DebugLogManager.GenerateErrorMessage<ObjectPoolManager>($"IPoolable 훅 처리 중 예외 발생 : {exception}");
                }
            }
        }

        private GameObject CreateInstance(string _key, GameObject _prefab)
        {
            if (_prefab == null)
            {
                return null;
            }

            return Instantiate(_prefab, Storage.GetOrCreateRoot(_key));
        }
        #endregion
    }
}
