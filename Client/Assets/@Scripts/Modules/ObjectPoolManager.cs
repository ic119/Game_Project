using Incheol.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace Incheol.Modules
{
    /// <summary>
    /// 풀링되는 프리팹의 컴포넌트가 구현하면, ObjectPoolController가 Get()/Release() 시점에
    /// 자동으로 호출해주는 초기화/정리 훅. 재사용 시 컴포넌트별 상태(파티클, 속도 등)를
    /// 초기화하고 싶을 때 이 인터페이스를 구현하면 된다.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>Get()으로 풀에서 대여되어 활성화된 직후 호출된다.</summary>
        void OnGetFromPool();

        /// <summary>Release()로 풀에 반환되어 비활성화되기 직전에 호출된다.</summary>
        void OnReleaseToPool();
    }

    public class ObjectPoolManager : SingletonObject<ObjectPoolManager>
    {
        /// <summary>
        /// 풀에 보관된 인스턴스는 씬 전환 후에도 계속 재사용해야 하므로, 이 매니저 자체가 씬 전환에도 파괴되지 않아야 한다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

        #region Variable
        /// <summary>
        /// Addressable Key -> 비활성 인스턴스 큐
        /// </summary>
        private readonly Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();

        /// 반환 시 어떤 풀에서 대여됐는지 역추적하기 위한 매핑
        private readonly Dictionary<GameObject, string> instanceKeyDictionary = new Dictionary<GameObject, string>();

        /// Addressable Key -> 해당 풀의 인스턴스를 보관할 부모 트랜스폼
        private readonly Dictionary<string, Transform> poolRootDictionary = new Dictionary<string, Transform>();

        /// <summary>
        /// Addressable Key -> 해당 풀이 보관할 수 있는 최대 비활성 인스턴스 수.
        /// 설정되어 있지 않으면 무제한.
        /// </summary>
        private readonly Dictionary<string, int> poolCapacityDictionary = new Dictionary<string, int>();

        private Transform poolRoot;
        #endregion

        #region LifeCycle
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

            Queue<GameObject> pool = GetOrCreatePoolQueue(_key);

            GameObject go = null;

            // 파괴된(null) 인스턴스가 남아 있을 수 있으므로 유효한 객체가 나올 때까지 꺼낸다.
            while (pool.Count > 0 && go == null)
            {
                go = pool.Dequeue();
            }

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
            instanceKeyDictionary[go] = _key;

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

            if (!instanceKeyDictionary.TryGetValue(_go, out string key))
            {
                DebugLogManager.GenerateErrorMessage<ObjectPoolManager>($"'{_go.name}'은(는) 풀에서 대여된 객체가 아닙니다.");
                return false;
            }

            instanceKeyDictionary.Remove(_go);

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

            _go.transform.SetParent(GetOrCreatePoolRoot(key), false);

            Queue<GameObject> pool = GetOrCreatePoolQueue(key);

            // 최대 보관 개수가 설정되어 있고 이미 가득 찼다면, 큐에 쌓아두는 대신 즉시 파괴해
            // 대여/반환이 반복될 때 큐가 무한정 커지는 것을 막는다.
            if (poolCapacityDictionary.TryGetValue(key, out int maxSize) && pool.Count >= maxSize)
            {
                Destroy(_go);
                return true;
            }

            pool.Enqueue(_go);
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
            if (poolDictionary.TryGetValue(_key, out Queue<GameObject> pool))
            {
                while (pool.Count > 0)
                {
                    GameObject go = pool.Dequeue();
                    if (go != null)
                    {
                        Destroy(go);
                    }
                }
                poolDictionary.Remove(_key);
            }

            if (_destroyActive)
            {
                // 활성 인스턴스까지 파괴하는 경우, 추적 정보 제거는 DestroyActiveByKey가 함께 처리한다.
                DestroyActiveByKey(_key);
            }
            else if (poolRootDictionary.TryGetValue(_key, out Transform activeRoot))
            {
                // 활성 인스턴스는 살려둬야 하므로, 곧 파괴할 pool root의 자식이라면 미리 분리해
                // 아래 Destroy(root.gameObject)에 딸려서 함께 파괴되지 않도록 한다.
                DetachActiveInstancesFrom(activeRoot);
            }

            if (poolRootDictionary.TryGetValue(_key, out Transform root))
            {
                if (root != null)
                {
                    Destroy(root.gameObject);
                }
                poolRootDictionary.Remove(_key);
            }

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
            // poolRootDictionary는 이 컨트롤러를 거쳐간 모든 Key를 담고 있으므로(풀 생성 시점에 항상 채워짐),
            // 아래에서 Clear되기 전에 핸들 해제 대상 Key 목록으로 미리 캡처해둔다.
            List<string> managedKeys = _releaseAddressableHandles ? new List<string>(poolRootDictionary.Keys) : null;

            foreach (KeyValuePair<string, Queue<GameObject>> pair in poolDictionary)
            {
                Queue<GameObject> pool = pair.Value;
                while (pool.Count > 0)
                {
                    GameObject go = pool.Dequeue();
                    if (go != null)
                    {
                        Destroy(go);
                    }
                }
            }
            poolDictionary.Clear();

            if (_destroyActive)
            {
                foreach (GameObject go in instanceKeyDictionary.Keys)
                {
                    if (go != null)
                    {
                        Destroy(go);
                    }
                }
                instanceKeyDictionary.Clear();
            }
            else
            {
                // 활성 인스턴스는 살려둬야 하므로, 곧 파괴할 poolRoot의 자식이라면 미리 분리해
                // 아래 Destroy(poolRoot.gameObject)에 딸려서 함께 파괴되지 않도록 한다.
                // (추적 정보는 instanceKeyDictionary에 그대로 남겨 이후 Release()로 정상 반환할 수 있게 한다)
                DetachActiveInstancesFrom(poolRoot);
            }

            if (poolRoot != null)
            {
                Destroy(poolRoot.gameObject);
                poolRoot = null;
            }
            poolRootDictionary.Clear();

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
            return !string.IsNullOrEmpty(_key) && poolDictionary.ContainsKey(_key);
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

            if (_maxSize <= 0)
            {
                poolCapacityDictionary.Remove(_key);
                return;
            }

            poolCapacityDictionary[_key] = _maxSize;
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

            Queue<GameObject> pool = GetOrCreatePoolQueue(_key);
            for (int i = 0; i < _count; i++)
            {
                GameObject go = CreateInstance(_key, _prefab);
                if (go == null)
                {
                    return;
                }

                go.SetActive(false);
                pool.Enqueue(go);
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

            return Instantiate(_prefab, GetOrCreatePoolRoot(_key));
        }

        private Queue<GameObject> GetOrCreatePoolQueue(string _key)
        {
            if (!poolDictionary.TryGetValue(_key, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                poolDictionary.Add(_key, pool);
            }
            return pool;
        }

        private Transform GetOrCreatePoolRoot(string _key)
        {
            if (this == null)
            {
                return null;
            }

            if (poolRoot == null)
            {
                poolRoot = new GameObject("@ObjectPools").transform;
                poolRoot.SetParent(transform, false);
            }

            if (!poolRootDictionary.TryGetValue(_key, out Transform root) || root == null)
            {
                root = new GameObject($"Pool_{_key}").transform;
                root.SetParent(poolRoot, false);
                poolRootDictionary[_key] = root;
            }

            return root;
        }

        private void DestroyActiveByKey(string _key)
        {
            List<GameObject> removeTargets = new List<GameObject>();
            foreach (KeyValuePair<GameObject, string> pair in instanceKeyDictionary)
            {
                if (pair.Value == _key)
                {
                    removeTargets.Add(pair.Key);
                }
            }

            foreach (GameObject go in removeTargets)
            {
                if (go != null)
                {
                    Destroy(go);
                }
                instanceKeyDictionary.Remove(go);
            }
        }

        /// <summary>
        /// 곧 파괴될 _root(또는 그 자식)에 매달려 있는, 여전히 추적 중인(대여 중인) 인스턴스를
        /// world position을 유지한 채 분리한다. Destroy(_root.gameObject) 호출 시 대여 중인
        /// 인스턴스까지 함께(연쇄적으로) 파괴되는 것을 막기 위한 용도이다.
        /// </summary>
        private void DetachActiveInstancesFrom(Transform _root)
        {
            if (_root == null)
            {
                return;
            }

            foreach (GameObject go in instanceKeyDictionary.Keys)
            {
                if (go != null && go.transform.IsChildOf(_root))
                {
                    go.transform.SetParent(null, true);
                }
            }
        }
        #endregion
    }
}