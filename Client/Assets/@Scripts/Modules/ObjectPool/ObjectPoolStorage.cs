using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// ObjectPoolManager의 보관 상태: 키별 비활성 인스턴스 큐, 대여 중인 인스턴스 추적, 키별 부모 트랜스폼, 풀 용량.
    /// Addressable 로드나 IPoolable 훅 같은 "어떻게 만들고 알릴지"는 모르고, 인스턴스를 어디에 보관하고 언제 파괴하는지만 맡는다.
    /// </summary>
    internal class ObjectPoolStorage
    {
        public const string PoolRootName = "@ObjectPools";

        private readonly MonoBehaviour owner;

        /// <summary>Addressable Key -> 비활성 인스턴스 큐</summary>
        private readonly Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();

        /// <summary>반환 시 어떤 풀에서 대여됐는지 역추적하기 위한 매핑(대여 중인 인스턴스)</summary>
        private readonly Dictionary<GameObject, string> instanceKeyDictionary = new Dictionary<GameObject, string>();

        /// <summary>Addressable Key -> 해당 풀의 인스턴스를 보관할 부모 트랜스폼</summary>
        private readonly Dictionary<string, Transform> poolRootDictionary = new Dictionary<string, Transform>();

        /// <summary>Addressable Key -> 최대 비활성 인스턴스 수. 설정이 없으면 무제한.</summary>
        private readonly Dictionary<string, int> poolCapacityDictionary = new Dictionary<string, int>();

        private Transform poolRoot;

        /// <param name="owner">풀 루트가 그 자식으로 만들어지는 ObjectPoolManager. 파괴된 뒤에는 새 루트를 만들지 않는다.</param>
        public ObjectPoolStorage(MonoBehaviour owner)
        {
            this.owner = owner;
        }

        public bool HasPool(string key)
        {
            return !string.IsNullOrEmpty(key) && poolDictionary.ContainsKey(key);
        }

        /// <summary>
        /// 풀이 추적 중인(대여 중이거나 풀에 보관 중인) 모든 Addressable Key. 씬 전환 시 Addressable 핸들을 일괄 정리할 때
        /// 여전히 사용 중인 프리팹을 보호하는 데 쓴다.
        /// </summary>
        public HashSet<string> GetTrackedKeys()
        {
            HashSet<string> keys = new HashSet<string>(poolDictionary.Keys);
            foreach (string key in instanceKeyDictionary.Values)
            {
                keys.Add(key);
            }
            return keys;
        }

        /// <summary>최대 보관 개수를 설정한다. 0 이하이면 무제한(설정 해제).</summary>
        public void SetCapacity(string key, int maxSize)
        {
            if (maxSize <= 0)
            {
                poolCapacityDictionary.Remove(key);
                return;
            }

            poolCapacityDictionary[key] = maxSize;
        }

        /// <summary>
        /// 풀에서 유효한 비활성 인스턴스를 하나 꺼낸다. 파괴된(null) 인스턴스가 남아 있을 수 있으므로 유효한 것이 나올 때까지 꺼내고,
        /// 없으면 null.
        /// </summary>
        public GameObject Dequeue(string key)
        {
            Queue<GameObject> pool = GetOrCreateQueue(key);
            GameObject go = null;

            while (pool.Count > 0 && go == null)
            {
                go = pool.Dequeue();
            }

            return go;
        }

        /// <summary>키의 풀(빈 큐)을 등록한다. 이미 있으면 아무 일도 하지 않는다.</summary>
        public void EnsurePool(string key)
        {
            GetOrCreateQueue(key);
        }

        /// <summary>미리 만들어 둔 비활성 인스턴스를 풀에 넣는다(용량 제한 없음).</summary>
        public void Enqueue(string key, GameObject go)
        {
            GetOrCreateQueue(key).Enqueue(go);
        }

        /// <summary>
        /// 반환된 인스턴스를 풀에 넣는다. 최대 보관 개수가 설정되어 있고 이미 가득 찼다면 넣지 않고 false를 돌려준다(호출한 쪽이 파괴한다).
        /// </summary>
        public bool TryEnqueueReleased(string key, GameObject go)
        {
            Queue<GameObject> pool = GetOrCreateQueue(key);

            if (poolCapacityDictionary.TryGetValue(key, out int maxSize) && pool.Count >= maxSize)
            {
                return false;
            }

            pool.Enqueue(go);
            return true;
        }

        /// <summary>대여한 인스턴스를 추적 대상으로 기록한다.</summary>
        public void Track(GameObject go, string key)
        {
            instanceKeyDictionary[go] = key;
        }

        /// <summary>대여 중인 인스턴스의 키를 찾는다. 있으면 추적에서 제거하고 true.</summary>
        public bool TryUntrack(GameObject go, out string key)
        {
            if (!instanceKeyDictionary.TryGetValue(go, out key))
            {
                return false;
            }

            instanceKeyDictionary.Remove(go);
            return true;
        }

        /// <summary>
        /// 키별 부모 트랜스폼을 가져오거나 만든다. 전체 루트("@ObjectPools")는 owner의 자식이다. owner가 파괴됐으면 null.
        /// </summary>
        public Transform GetOrCreateRoot(string key)
        {
            if (owner == null)
            {
                return null;
            }

            if (poolRoot == null)
            {
                poolRoot = new GameObject(PoolRootName).transform;
                poolRoot.SetParent(owner.transform, false);
            }

            if (!poolRootDictionary.TryGetValue(key, out Transform root) || root == null)
            {
                root = new GameObject($"Pool_{key}").transform;
                root.SetParent(poolRoot, false);
                poolRootDictionary[key] = root;
            }

            return root;
        }

        /// <summary>
        /// 특정 키의 풀을 비우고 제거한다.
        /// </summary>
        /// <param name="destroyActive">true이면 대여 중(활성)인 인스턴스까지 파괴</param>
        public void DestroyPool(string key, bool destroyActive)
        {
            if (poolDictionary.TryGetValue(key, out Queue<GameObject> pool))
            {
                DestroyAndClear(pool);
                poolDictionary.Remove(key);
            }

            if (destroyActive)
            {
                // 활성 인스턴스까지 파괴하는 경우, 추적 정보 제거는 DestroyActiveByKey가 함께 처리한다.
                DestroyActiveByKey(key);
            }
            else if (poolRootDictionary.TryGetValue(key, out Transform activeRoot))
            {
                // 활성 인스턴스는 살려둬야 하므로, 곧 파괴할 pool root의 자식이라면 미리 분리해
                // 아래 Destroy(root.gameObject)에 딸려서 함께 파괴되지 않도록 한다.
                DetachActiveInstancesFrom(activeRoot);
            }

            if (poolRootDictionary.TryGetValue(key, out Transform root))
            {
                if (root != null)
                {
                    Object.Destroy(root.gameObject);
                }
                poolRootDictionary.Remove(key);
            }
        }

        /// <summary>
        /// 모든 풀을 비우고 제거한다.
        /// </summary>
        /// <param name="destroyActive">true이면 대여 중(활성)인 인스턴스까지 파괴</param>
        /// <param name="captureKeys">true이면 정리 전에 풀이 거쳐간 모든 Key 목록을 돌려준다(Addressable 핸들 해제용). 아니면 null.</param>
        public List<string> DestroyAll(bool destroyActive, bool captureKeys)
        {
            // poolRootDictionary는 풀을 거쳐간 모든 Key를 담고 있으므로(풀 생성 시점에 항상 채워짐),
            // 아래에서 Clear되기 전에 핸들 해제 대상 Key 목록으로 미리 캡처해둔다.
            List<string> managedKeys = captureKeys ? new List<string>(poolRootDictionary.Keys) : null;

            foreach (KeyValuePair<string, Queue<GameObject>> pair in poolDictionary)
            {
                DestroyAndClear(pair.Value);
            }
            poolDictionary.Clear();

            if (destroyActive)
            {
                foreach (GameObject go in instanceKeyDictionary.Keys)
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
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
                Object.Destroy(poolRoot.gameObject);
                poolRoot = null;
            }
            poolRootDictionary.Clear();

            return managedKeys;
        }

        private Queue<GameObject> GetOrCreateQueue(string key)
        {
            if (!poolDictionary.TryGetValue(key, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                poolDictionary.Add(key, pool);
            }
            return pool;
        }

        private static void DestroyAndClear(Queue<GameObject> pool)
        {
            while (pool.Count > 0)
            {
                GameObject go = pool.Dequeue();
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }
        }

        private void DestroyActiveByKey(string key)
        {
            List<GameObject> removeTargets = new List<GameObject>();
            foreach (KeyValuePair<GameObject, string> pair in instanceKeyDictionary)
            {
                if (pair.Value == key)
                {
                    removeTargets.Add(pair.Key);
                }
            }

            foreach (GameObject go in removeTargets)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
                instanceKeyDictionary.Remove(go);
            }
        }

        /// <summary>
        /// 곧 파괴될 root(또는 그 자식)에 매달려 있는, 여전히 추적 중인(대여 중인) 인스턴스를 world position을 유지한 채 분리한다.
        /// Destroy(root.gameObject) 호출 시 대여 중인 인스턴스까지 함께(연쇄적으로) 파괴되는 것을 막기 위한 용도이다.
        /// </summary>
        private void DetachActiveInstancesFrom(Transform root)
        {
            if (root == null)
            {
                return;
            }

            foreach (GameObject go in instanceKeyDictionary.Keys)
            {
                if (go != null && go.transform.IsChildOf(root))
                {
                    go.transform.SetParent(null, true);
                }
            }
        }
    }
}
