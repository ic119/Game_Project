using JJORY.Module;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 단일 프리팹에 대한 간단한 오브젝트 풀
/// 동일 GameObject에 붙여두고, 다른 스크립트에서 Get/Return으로 사용
/// </summary>
public class ObjectPool : MonoBehaviour
{
	[SerializeField] private GameObject prefab;
	[SerializeField, Min(0)] private int initialSize = 20;
	[SerializeField] private bool prewarmOnAwake = true;
	
	[Header("Addressables")]
	[SerializeField] private bool useAddressables = false;
	[SerializeField] private string addressKey = string.Empty;
	[SerializeField] private bool loadKeyOnAwake = true;

	private readonly Queue<GameObject> pooledQueue = new Queue<GameObject>();

	private void Awake()
	{
		if (useAddressables && loadKeyOnAwake && !string.IsNullOrEmpty(addressKey) && AddressableController.Instance != null)
		{
			// 비동기 로드 요청
			AddressableController.Instance.LoadPrefab<GameObject>(addressKey);
		}

		if (!useAddressables)
		{
			if (prewarmOnAwake)
			{
				Prewarm();
			}
		}
		else
		{
			if (prewarmOnAwake)
			{
				StartCoroutine(PrewarmAddressablesCoroutine());
			}
		}
	}

	/// <summary>
	/// 초기 사이즈만큼 미리 생성하여 풀에 적재
	/// </summary>
	public void Prewarm()
	{
		if (prefab == null) return;

		int target = Mathf.Max(0, initialSize);
		for (int i = pooledQueue.Count; i < target; i++)
		{
			GameObject instance = CreateInstance();
			if (instance == null) continue;
			instance.SetActive(false);
			pooledQueue.Enqueue(instance);
		}
	}

	private IEnumerator PrewarmAddressablesCoroutine()
	{
		if (string.IsNullOrEmpty(addressKey) || AddressableController.Instance == null)
		{
			yield break;
		}

		// 키가 로드될 때까지 대기
		while (!AddressableController.Instance.GetHandler(addressKey, out var _))
		{
			yield return null;
		}

		int target = Mathf.Max(0, initialSize);
		for (int i = pooledQueue.Count; i < target; i++)
		{
			GameObject instance = CreateInstance();
			if (instance == null)
			{
				// 아직 인스턴스화 불가하면 다음 프레임에 재시도
				yield return null;
				i--;
				continue;
			}
			instance.SetActive(false);
			pooledQueue.Enqueue(instance);
			yield return null;
		}
	}

	/// <summary>
	/// 오브젝트를 풀에서 하나 꺼냄. 없으면 새로 생성
	/// </summary>
	public GameObject Get()
	{
		if (!useAddressables && prefab == null)
		{
			Debug.LogWarning("[ObjectPool] Prefab이 지정되지 않았습니다.", this);
			return null;
		}

		GameObject instance = pooledQueue.Count > 0 ? pooledQueue.Dequeue() : CreateInstance();
		if (instance == null) return null;

		instance.SetActive(true);
		return instance;
	}

	/// <summary>
	/// 오브젝트를 풀에 반납
	/// </summary>
	public void Return(GameObject instance)
	{
		if (instance == null) return;
		instance.SetActive(false);
		pooledQueue.Enqueue(instance);
	}

	private GameObject CreateInstance()
	{
		if (useAddressables)
		{
			if (AddressableController.Instance == null || string.IsNullOrEmpty(addressKey))
			{
				Debug.LogWarning("[ObjectPool] Addressables 사용 설정이 되어 있으나 AddressableController 또는 Key가 유효하지 않습니다.", this);
				return null;
			}
			GameObject addrGo = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(addressKey, transform);
			if (addrGo == null)
			{
				// 아직 로드 전일 가능성
				return null;
			}
			AttachPooledObject(addrGo);
			addrGo.name = $"{addressKey}_Pooled";
			return addrGo;  
		}

		GameObject go = Instantiate(prefab, transform);
		go.name = $"{prefab.name}_Pooled";
		AttachPooledObject(go);
		return go;
	}

	private void AttachPooledObject(GameObject go)
	{
		PooledObject marker = go.GetComponent<PooledObject>();
		if (marker == null)
		{
			marker = go.AddComponent<PooledObject>();
		}
		marker.SetOwnerPool(this);
	}

	// 인스펙터에서 런타임 확인용
	public int GetCachedCount() => pooledQueue.Count;
}

