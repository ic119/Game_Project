using Incheol.Modules;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 로컬 플레이어 전용. C 입력으로 2타 콤보 공격을 수행한다.
    /// 애니메이션은 BasicCharacterStance(Animator Controller)의 Attack Layer를 그대로 사용한다:
    /// AttackLayerIdle(ComboIndex=0) -[ComboIndex=1]-> Attack1 -[ComboIndex=2]-> Attack2 -[ComboIndex=0]-> AttackLayerIdle.
    /// Attack Layer는 기본 weight가 0이라(그래야 평소 Base Layer의 이동/대기 애니메이션을 가리지 않음),
    /// 콤보가 진행되는 동안만 1로 올렸다가 콤보가 끝나면 다시 0으로 내린다.
    /// 각 타수마다 전방 구체 판정(OverlapSphere)으로 가장 가까운 원격 플레이어를 찾아 공격 요청을 보낸다.
    /// 데미지 적용은 하지 않는다 - 서버가 중계한 Game_DamageBroadcast를 받는 쪽에서 처리한다.
    /// </summary>
    public class PlayerAttackController : MonoBehaviour
    {
        [Header("공격 판정")]
        [SerializeField] private KeyCode attackKey = KeyCode.C;
        [SerializeField, Min(0f)] private float attackRange = 1.5f;
        [SerializeField, Min(0f)] private float attackRadius = 1.0f;

        [Header("콤보 (BasicCharacterStance/Attack Layer 참고)")]
        [Tooltip("콤보 최대 타수. Attack Layer에 Attack1/Attack2 두 단계만 있어 2로 둔다.")]
        [SerializeField, Min(1)] private int maxComboStage = 2;
        [Tooltip("각 콤보 단계 애니메이션 길이(초). Attack01/02_SingleSword 클립 길이(약 0.53초)에 맞춘 값.")]
        [SerializeField, Min(0.05f)] private float comboStageDuration = 0.55f;
        [Tooltip("각 단계 시작 후 이 시간이 지나야 다음 입력을 콤보 연계로 인정한다(스윙 시작 직후 캔슬 방지).")]
        [SerializeField, Min(0f)] private float comboInputGuard = 0.15f;
        [Tooltip("단계 종료 후 이 시간 안에 다음 입력이 없으면 콤보가 끊기고 Idle로 돌아간다.")]
        [SerializeField, Min(0f)] private float comboWindowGrace = 0.2f;

        private const string AttackLayerName = "Attack Layer";
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly Collider[] overlapBuffer = new Collider[16];

        private Animator animator;
        private int attackLayerIndex = -1;

        private int comboStage;
        private float stageStartTime;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator != null)
            {
                attackLayerIndex = animator.GetLayerIndex(AttackLayerName);
            }
        }

        private void Update()
        {
            // 단계 시간 + 유예시간이 지나도록 다음 입력이 없었다면 콤보가 끊긴 것으로 보고 Idle로 되돌린다.
            if (comboStage > 0 && Time.time >= stageStartTime + comboStageDuration + comboWindowGrace)
            {
                ResetCombo();
            }

            if (!Input.GetKeyDown(attackKey))
            {
                return;
            }

            if (comboStage == 0)
            {
                StartCombo();
            }
            else if (comboStage < maxComboStage && Time.time >= stageStartTime + comboInputGuard)
            {
                AdvanceCombo();
            }
            // 가드 시간 이전(너무 이른 연타)이거나 이미 마지막 타수인 입력은 무시한다.
        }

        private void StartCombo()
        {
            comboStage = 1;
            stageStartTime = Time.time;

            if (animator != null && attackLayerIndex >= 0)
            {
                animator.SetLayerWeight(attackLayerIndex, 1f);
                animator.SetInteger(ComboIndexHash, comboStage);
            }

            RequestAttack();
        }

        private void AdvanceCombo()
        {
            comboStage++;
            stageStartTime = Time.time;

            if (animator != null && attackLayerIndex >= 0)
            {
                animator.SetInteger(ComboIndexHash, comboStage);
            }

            RequestAttack();
        }

        private void ResetCombo()
        {
            comboStage = 0;

            if (animator != null && attackLayerIndex >= 0)
            {
                animator.SetInteger(ComboIndexHash, 0);
                animator.SetLayerWeight(attackLayerIndex, 0f);
            }
        }

        private void RequestAttack()
        {
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
