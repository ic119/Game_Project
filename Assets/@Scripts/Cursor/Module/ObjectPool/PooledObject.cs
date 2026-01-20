using UnityEngine;

/// <summary>
/// 자신을 생성한 풀에 대한 참조를 보관하고 필요 시 반납
/// </summary>
public class PooledObject : MonoBehaviour
{
	private ObjectPool ownerPool;

	public void SetOwnerPool(ObjectPool pool)
	{
		ownerPool = pool;
	}

	public void ReturnToPool()
	{
		if (ownerPool != null)
		{
			ownerPool.Return(gameObject);
		}
		else
		{
			// 예외: 풀 참조가 없으면 비활성만 수행
			gameObject.SetActive(false);
		}
	}
}

