using Incheol.Models.Define;
using Incheol.Modules;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 키보드 화살표로만 캐릭터를 이동/회전시킨다. Rigidbody를 통해 중력의 영향을 받아 지면에 착지하고,
    /// CapsuleCollider로 맵 오브젝트와 충돌 처리된다. 둘 다 이 스크립트가 추가되는 시점에 RequireComponent로 함께 생성된다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerMoveController : MonoBehaviour
    {
        #region Variable
        [Header("Move")]
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float rotateSpeed = 150f;

        [Header("Dash")]
        [SerializeField] private KeyCode dashKey = KeyCode.Space;
        [Tooltip("한 번의 대쉬로 이동하는 총 거리(m). dashDuration 동안 이 거리만큼 전방으로 이동하도록 속도가 계산된다.")]
        [SerializeField, Min(0f)] private float dashDistance = 3f;
        [SerializeField, Min(0f)] private float dashDuration = 0.25f;
        [SerializeField, Min(0f)] private float dashCooldown = 1f;

        [Header("Dash Effect")]
        [Tooltip("Dash01 이펙트를 재생할 위치. 캐릭터 기준 로컬 오프셋이며, 캐릭터의 현재 방향에 맞춰 회전 적용된다. " +
            "캐릭터 원점이 발밑이므로 기본값(0,0,0)이면 바닥면 그대로에 재생된다.")]
        [SerializeField] private Vector3 dashEffectPositionOffset = Vector3.zero;
        [Tooltip("Dash01 이펙트 프리팹에 이미 적용된 크기 위에 추가로 곱해지는 배율. 1이면 프리팹 크기 그대로.")]
        [SerializeField, Min(0.01f)] private float dashEffectScale = 0.45f;

        private Rigidbody rigidBody;
        private Animator animator;

        private float moveInput;
        private float turnInput;

        private bool isDashing;
        private float dashEndTime;
        private float nextDashReadyTime;
        private float currentDashSpeed;

        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
        private static readonly int IsDashHash = Animator.StringToHash("IsDash");
        #endregion

        #region LifeCycle
        private void Awake()
        {
            rigidBody = GetComponent<Rigidbody>();
            rigidBody.useGravity = true;
            // 이동/회전은 이 스크립트가 직접 제어하므로, 충돌로 캐릭터가 넘어지지 않도록 X/Z 회전만 고정한다(Y는 회전 제어에 필요).
            rigidBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rigidBody.interpolation = RigidbodyInterpolation.Interpolate;

            CapsuleCollider capsuleCollider = GetComponent<CapsuleCollider>();
            capsuleCollider.height = 1.8f;
            capsuleCollider.radius = 0.4f;
            capsuleCollider.center = new Vector3(0f, 0.9f, 0f);

            animator = GetComponent<Animator>();
            if (animator != null)
            {
                // 애니메이션에 포함된 루트 모션과 Rigidbody 이동이 겹쳐 밀리지 않도록, 이동은 전적으로 이 스크립트가 담당한다.
                animator.applyRootMotion = false;
            }
        }

        private void Update()
        {
            moveInput = 0f;
            if (Input.GetKey(KeyCode.UpArrow))
            {
                moveInput += 1f;
            }
            if (Input.GetKey(KeyCode.DownArrow))
            {
                moveInput -= 1f;
            }

            turnInput = 0f;
            if (Input.GetKey(KeyCode.RightArrow))
            {
                turnInput += 1f;
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                turnInput -= 1f;
            }

            if (isDashing && Time.time >= dashEndTime)
            {
                EndDash();
            }
            else if (!isDashing && Input.GetKeyDown(dashKey) && Time.time >= nextDashReadyTime)
            {
                StartDash();
            }

            UpdateAnimatorState();
        }

        private void FixedUpdate()
        {
            if (isDashing)
            {
                // 대쉬 중에는 화살표 입력(이동/회전)을 무시하고, 캐릭터가 바라보고 있는 전방으로만 이동한다.
                Vector3 dashDelta = transform.forward * (currentDashSpeed * Time.fixedDeltaTime);
                rigidBody.MovePosition(rigidBody.position + dashDelta);
                return;
            }

            if (turnInput != 0f)
            {
                Quaternion turnDelta = Quaternion.Euler(0f, turnInput * rotateSpeed * Time.fixedDeltaTime, 0f);
                rigidBody.MoveRotation(rigidBody.rotation * turnDelta);
            }

            if (moveInput != 0f)
            {
                Vector3 moveDelta = transform.forward * (moveInput * moveSpeed * Time.fixedDeltaTime);
                rigidBody.MovePosition(rigidBody.position + moveDelta);
            }
        }
        #endregion

        #region Method
        private void UpdateAnimatorState()
        {
            if (animator == null)
            {
                return;
            }

            bool isMoving = moveInput != 0f || turnInput != 0f;
            animator.SetBool(IsMoveHash, isMoving);
            animator.SetBool(IsIdleHash, !isMoving);
            animator.SetBool(IsDashHash, isDashing);
        }

        /// <summary>
        /// Space 입력으로 캐릭터 전방 대쉬를 시작한다. BasicCharacterStance의 IsDash(bool)를 켜서
        /// AnyState -> Dash 전환을 트리거하고, 캐릭터 바닥면에 Dash01 이펙트를 재생한다.
        /// dashCooldown이 지나기 전까지는 재입력을 받지 않는다.
        /// </summary>
        private void StartDash()
        {
            isDashing = true;
            dashEndTime = Time.time + dashDuration;
            nextDashReadyTime = dashEndTime + dashCooldown;
            // dashDuration이 0으로 설정된 경우(즉시 대쉬) 나눗셈을 피한다 - 어차피 다음 프레임에 바로 EndDash된다.
            currentDashSpeed = dashDuration > 0f ? dashDistance / dashDuration : 0f;

            PlayDashEffect();
        }

        /// <summary>
        /// dashDuration이 지나면 Update에서 호출된다. IsDash가 꺼지면 애니메이터는 그 시점의
        /// IsMove/IsIdle 값에 따라 Move/Idle로 자동 전환된다(Jump 종료 처리와 동일한 패턴).
        /// </summary>
        private void EndDash()
        {
            isDashing = false;
        }

        /// <summary>
        /// Dash01 이펙트를 dashEffectPositionOffset만큼 캐릭터 기준으로 옮긴 위치(기본값은 발밑=바닥면)에,
        /// 현재 바라보는 방향으로 재생한다. dashEffectScale로 크기를 추가 조절할 수 있다.
        /// </summary>
        private void PlayDashEffect()
        {
            if (ObjectPoolManager.Instance == null)
            {
                return;
            }

            Vector3 effectPosition = transform.TransformPoint(dashEffectPositionOffset);
            GameObject effectInstance = ObjectPoolManager.Instance.Get(AddressableAssetKey.Dash01.ToString(), effectPosition, transform.rotation);

            // 풀에서 돌려받은 인스턴스는 프리팹 원본 크기로 초기화되어 있으므로, dashEffectScale은 그 위에 곱해지는 배율이다.
            if (effectInstance != null && !Mathf.Approximately(dashEffectScale, 1f))
            {
                effectInstance.transform.localScale *= dashEffectScale;
            }
        }
        #endregion
    }
}
