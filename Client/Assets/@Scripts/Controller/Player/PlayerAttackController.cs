using Incheol.Modules;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 로컬 플레이어 전용. C 입력 시 전방 구체 판정(OverlapSphere)으로 가장 가까운 원격 플레이어를
    /// 찾아 GameServer에 공격 요청(Game_AttackRequest)을 보낸다. 데미지 적용은 하지 않는다 - 서버가
    /// 중계한 Game_DamageBroadcast를 받는 쪽(RemotePlayerManager/GameSceneManager)에서 처리한다.
    /// RemoteCharacterController가 있는 대상만 맞는다(로컬 자신은 이 컴포넌트가 없어 자동으로 제외됨).
    /// </summary>
    public class PlayerAttackController : MonoBehaviour
    {
        [Header("공격 판정")]
        [SerializeField] private KeyCode attackKey = KeyCode.C;
        [SerializeField, Min(0f)] private float attackRange = 1.5f;
        [SerializeField, Min(0f)] private float attackRadius = 1.0f;
        [SerializeField, Min(0f)] private float attackCooldown = 0.6f;

        private static readonly Collider[] overlapBuffer = new Collider[16];

        private float nextAttackTime;

        private void Update()
        {
            if (Time.time < nextAttackTime)
            {
                return;
            }

            if (Input.GetKeyDown(attackKey))
            {
                TryAttack();
            }
        }

        private void TryAttack()
        {
            nextAttackTime = Time.time + attackCooldown;

            Vector3 origin = transform.position + transform.forward * attackRange;
            int hitCount = Physics.OverlapSphereNonAlloc(origin, attackRadius, overlapBuffer);

            long? targetId = FindNearestTargetId(hitCount);
            if (!targetId.HasValue)
            {
                return;
            }

            GameServerConnectManager.Instance?.SendAttack(targetId.Value);
        }

        private long? FindNearestTargetId(int hitCount)
        {
            long? bestId = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                RemoteCharacterController remote = hit.GetComponentInParent<RemoteCharacterController>();
                if (remote == null)
                {
                    continue;
                }

                float distanceSqr = (remote.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestId = remote.PlayerId;
                }
            }

            return bestId;
        }
    }
}
