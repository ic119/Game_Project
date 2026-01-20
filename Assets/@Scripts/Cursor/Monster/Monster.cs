using UnityEngine;

[DisallowMultipleComponent]
public class Monster : MonoBehaviour
{
	[SerializeField, Tooltip("0이면 자동 반납하지 않습니다.")] private float autoReturnAfterSeconds = 0f;

	private PooledObject pooledMarker;
	private float remainSeconds;
	private bool autoReturnActive;

	private void Awake()
	{
		pooledMarker = GetComponent<PooledObject>();
	}

	private void OnEnable()
	{
		remainSeconds = autoReturnAfterSeconds;
		autoReturnActive = autoReturnAfterSeconds > 0f;
	}

	private void Update()
	{
		if (!autoReturnActive) return;
		remainSeconds -= Time.deltaTime;
		if (remainSeconds <= 0f)
		{
			autoReturnActive = false;
			pooledMarker?.ReturnToPool();
		}
	}

	/// <summary>
	/// 외부에서 처치/소멸 처리 시 호출
	/// </summary>
	public void Kill()
	{
		pooledMarker?.ReturnToPool();
	}
}

