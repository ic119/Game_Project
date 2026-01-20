using UnityEngine;

[DisallowMultipleComponent]
public class MonsterAIController : MonoBehaviour
{
	[Header("Target Detection")]
	[SerializeField] private string playerTag = "Player";
	[SerializeField] private LayerMask targetMask = ~0;
	[SerializeField, Min(0f)] private float detectionRadius = 8f;
	[SerializeField, Min(0f)] private float attackRange = 2f;
	[SerializeField] private bool useLineOfSight = false;
	[SerializeField] private LayerMask obstacleMask = 0;
	[SerializeField, Min(0f)] private float eyeHeight = 1.2f;

	[Header("Attack")]
	[SerializeField, Min(0.1f)] private float attackInterval = 1.5f;
	[SerializeField, Min(0f)] private float attackDamage = 10f;
	[SerializeField] private string attackTriggerName = "Attack";

	private Transform target;
	private Animator animator;
	private float lastAttackTime = -999f;
	private static readonly Collider[] overlapBuffer = new Collider[8];

	private void Awake()
	{
		animator = GetComponentInChildren<Animator>();
	}

	private void Update()
	{
		if (target == null || !target.gameObject.activeInHierarchy)
		{
			TryAcquireTarget();
		}

		if (target == null) return;

		Vector3 toTarget = target.position - transform.position;
		toTarget.y = 0f;

		float distance = toTarget.magnitude;

		// 간단히 바라보기
		if (distance > 0.001f)
		{
			Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
			transform.rotation = Quaternion.Slerp(transform.rotation, look, 10f * Time.deltaTime);
		}

		// 공격 조건 체크
		if (distance <= attackRange && Time.time >= lastAttackTime + attackInterval)
		{
			if (!useLineOfSight || HasLineOfSight(target))
			{
				PerformAttack();
			}
		}
	}

	private void TryAcquireTarget()
	{
		int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, overlapBuffer, targetMask, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < count; i++)
		{
			Collider col = overlapBuffer[i];
			if (col == null) continue;
			if (!string.IsNullOrEmpty(playerTag) && !col.CompareTag(playerTag)) continue;
			target = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform;
			if (!useLineOfSight || HasLineOfSight(target))
			{
				break;
			}
			else
			{
				target = null;
			}
		}
	}

	private bool HasLineOfSight(Transform t)
	{
		if (!useLineOfSight) return true;
		if (obstacleMask == 0) return true;

		Vector3 origin = transform.position + Vector3.up * eyeHeight;
		Vector3 dest = t.position + Vector3.up * eyeHeight;
		Vector3 dir = dest - origin;
		float dist = dir.magnitude;
		dir /= Mathf.Max(0.0001f, dist);

		return !Physics.Raycast(origin, dir, dist, obstacleMask, QueryTriggerInteraction.Ignore);
	}

	private void PerformAttack()
	{
		lastAttackTime = Time.time;
		if (animator != null && !string.IsNullOrEmpty(attackTriggerName))
		{
			animator.SetTrigger(attackTriggerName);
		}
		// 간단 즉시 피해 적용 (애니메이션 이벤트로 분리 가능)
		if (target != null)
		{
			IDamageable dmg = target.GetComponent<IDamageable>();
			if (dmg != null && attackDamage > 0f)
			{
				dmg.ApplyDamage(attackDamage);
			}
		}
	}

	// 애니메이션 이벤트에서 호출 가능
	public void AnimationEvent_DealDamage()
	{
		if (target == null) return;
		IDamageable dmg = target.GetComponent<IDamageable>();
		if (dmg != null && attackDamage > 0f)
		{
			dmg.ApplyDamage(attackDamage);
		}
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.25f);
		Gizmos.DrawSphere(transform.position, detectionRadius);
		Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.35f);
		Gizmos.DrawSphere(transform.position, attackRange);
	}
}

