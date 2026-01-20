using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ObjectPool))]
public class MonsterSpawner : MonoBehaviour
{
	[Header("Spawn Settings")]
	[SerializeField, Min(1)] private int spawnCount = 20;
	[SerializeField] private bool spawnOnStart = true;
	[SerializeField] private bool faceRandomDirection = false;

	[Header("Area (XZ 사각형)")]
	[SerializeField] private Vector3 areaCenterOffset = Vector3.zero;
	[SerializeField] private Vector2 areaSize = new Vector2(20f, 20f);
	[SerializeField] private float yOffset = 0f;

	private ObjectPool pool;
	private readonly List<GameObject> spawned = new List<GameObject>(64);

	private void Awake()
	{
		pool = GetComponent<ObjectPool>();
		if (pool == null)
		{
			Debug.LogWarning("[MonsterSpawner] ObjectPool 컴포넌트를 찾지 못했습니다.", this);
		}
	}

	private void Start()
	{
		if (spawnOnStart)
		{
			SpawnAll();
		}
	}

	public void SpawnAll()
	{
		if (pool == null) return;
		for (int i = 0; i < spawnCount; i++)
		{
			SpawnOne();
		}
	}

	public GameObject SpawnOne()
	{
		if (pool == null) return null;
		GameObject obj = pool.Get();
		if (obj == null) return null;

		obj.transform.position = GetRandomPosition();
		if (faceRandomDirection)
		{
			float y = Random.Range(0f, 360f);
			obj.transform.rotation = Quaternion.Euler(0f, y, 0f);
		}
		spawned.Add(obj);
		return obj;
	}

	public void DespawnAll()
	{
		for (int i = 0; i < spawned.Count; i++)
		{
			GameObject obj = spawned[i];
			if (obj == null) continue;
			PooledObject marker = obj.GetComponent<PooledObject>();
			if (marker != null)
			{
				marker.ReturnToPool();
			}
			else
			{
				obj.SetActive(false);
			}
		}
		spawned.Clear();
	}

	private Vector3 GetRandomPosition()
	{
		Vector3 center = transform.position + areaCenterOffset;
		float halfX = Mathf.Max(0f, areaSize.x) * 0.5f;
		float halfZ = Mathf.Max(0f, areaSize.y) * 0.5f;
		float x = Random.Range(-halfX, halfX);
		float z = Random.Range(-halfZ, halfZ);
		return new Vector3(center.x + x, center.y + yOffset, center.z + z);
	}

	private void OnDrawGizmosSelected()
	{
		// 스폰 영역 시각화 (XZ 평면)
		Gizmos.color = new Color(0.1f, 0.8f, 0.2f, 0.35f);
		Vector3 center = transform.position + areaCenterOffset + Vector3.up * yOffset;
		Vector3 size = new Vector3(Mathf.Max(0f, areaSize.x), 0.01f, Mathf.Max(0f, areaSize.y));
		Gizmos.DrawCube(center, size);
		Gizmos.color = new Color(0.1f, 0.8f, 0.2f, 0.9f);
		Gizmos.DrawWireCube(center, size);
	}
}

