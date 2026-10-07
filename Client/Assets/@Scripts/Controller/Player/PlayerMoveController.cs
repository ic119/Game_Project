using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// WASD로 캐릭터를 카메라 기준으로 이동시킨다(W = 화면 위쪽, D = 화면 오른쪽). 캐릭터는 이동하는 방향으로
    /// 스스로 몸을 돌린다. 카메라는 고정 각도라 캐릭터가 돌아도 화면이 돌지 않는다. Rigidbody를 통해 중력의 영향을 받아
    /// 지면에 착지하고, CapsuleCollider로 맵 오브젝트와 충돌 처리된다. 둘 다 이 스크립트가 추가되는 시점에
    /// RequireComponent로 함께 생성된다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerMoveController : MonoBehaviour
    {
        #region Variable
        [Header("Move")]
        [SerializeField] private KeyCode moveUpKey = KeyCode.W;
        [SerializeField] private KeyCode moveDownKey = KeyCode.S;
        [SerializeField] private KeyCode moveLeftKey = KeyCode.A;
        [SerializeField] private KeyCode moveRightKey = KeyCode.D;
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [Tooltip("이동 방향으로 몸을 돌리는 속도(도/초). 너무 낮으면 방향을 바꿀 때 옆으로 미끄러지는 것처럼 보인다.")]
        [SerializeField, Min(0f)] private float rotateSpeed = 720f;
        [Tooltip("씬에 메인 카메라를 찾을 수 없을 때 입력 기준으로 쓸 카메라의 수평 회전(도). 고정 쿼터뷰 카메라의 각도와 같아야 한다.")]
        [SerializeField] private float fallbackCameraYaw = 45f;

        [Header("Dash")]
        [SerializeField] private KeyCode dashKey = KeyCode.Space;
        [Tooltip("한 번의 대쉬로 이동하는 총 거리(m). dashDuration 동안 이 거리만큼 대쉬 방향으로 이동하도록 속도가 계산된다.")]
        [SerializeField, Min(0f)] private float dashDistance = 3f;
        [SerializeField, Min(0f)] private float dashDuration = CombatTimings.DashDurationSeconds;
        [SerializeField, Min(0f)] private float dashCooldown = CombatTimings.DashCooldownSeconds;

        [Header("Dash Effect")]
        [Tooltip("Dash01 이펙트를 재생할 위치. 캐릭터 기준 로컬 오프셋이며, 캐릭터의 현재 방향에 맞춰 회전 적용된다. " +
            "캐릭터 원점이 발밑이므로 기본값(0,0,0)이면 바닥면 그대로에 재생된다.")]
        [SerializeField] private Vector3 dashEffectPositionOffset = Vector3.zero;
        [Tooltip("Dash01 이펙트 프리팹에 이미 적용된 크기 위에 추가로 곱해지는 배율. 1이면 프리팹 크기 그대로.")]
        [SerializeField, Min(0.01f)] private float dashEffectScale = 0.45f;

        private Rigidbody rigidBody;
        private Animator animator;
        private Transform cameraTransform;

        // 이번 프레임 입력이 가리키는 월드 방향(수평, 정규화). 입력이 없으면 zero.
        private Vector3 moveDirection;

        // 이 시각(Time.time)까지는 이동 방향으로 몸을 돌리지 않는다. 공격 방향 보조(FaceDirection)가 정한 방향을 이동 입력이 곧바로 덮어쓰지 않게 한다.
        private float facingLockUntil;

        private bool isDashing;
        private Vector3 dashDirection;
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
            // 회전은 이 스크립트가 MoveRotation으로만 직접 제어한다(물리 힘과 무관하게 목표 회전을 그대로 적용하므로
            // Y축을 Freeze해도 이동 방향 회전에는 영향이 없다). X/Y/Z를 전부 고정해, 몬스터/벽 등과 충돌했을 때
            // 물리 엔진이 계산한 충돌 토크가 Y축 회전에 새어 들어가 캐릭터가 의도치 않게 돌아가는 문제를 막는다.
            rigidBody.constraints = RigidbodyConstraints.FreezeRotation;
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
            // 채팅 입력 중처럼 게임플레이 입력이 막혀 있으면(InputBlocker) 방향키/대쉬 키를 아예 읽지 않는다 - 입력이 0으로 남아
            // 이동이 멈추고(이미 진행 중인 대쉬는 그대로 끝난다), 입력창에서 쓰는 방향키/Space가 이동/대쉬로 새지 않는다.
            bool inputBlocked = InputBlocker.IsBlocked;

            Vector2 input = Vector2.zero;
            if (!inputBlocked && Input.GetKey(moveUpKey))
            {
                input.y += 1f;
            }
            if (!inputBlocked && Input.GetKey(moveDownKey))
            {
                input.y -= 1f;
            }
            if (!inputBlocked && Input.GetKey(moveRightKey))
            {
                input.x += 1f;
            }
            if (!inputBlocked && Input.GetKey(moveLeftKey))
            {
                input.x -= 1f;
            }

            moveDirection = ToWorldDirection(input);

            if (isDashing && Time.time >= dashEndTime)
            {
                EndDash();
            }
            else if (!inputBlocked && !isDashing && Input.GetKeyDown(dashKey) && Time.time >= nextDashReadyTime)
            {
                StartDash();
            }

            UpdateAnimatorState();
        }

        private void FixedUpdate()
        {
            if (isDashing)
            {
                // 대쉬 중에는 방향키 입력을 무시하고, 대쉬를 시작할 때 정한 방향으로만 이동한다.
                Vector3 dashDelta = dashDirection * (currentDashSpeed * Time.fixedDeltaTime);
                rigidBody.MovePosition(rigidBody.position + dashDelta);
                return;
            }

            if (moveDirection == Vector3.zero)
            {
                return;
            }

            // 이동 방향으로 몸을 돌리고(최대 rotateSpeed), 입력 방향으로 곧바로 이동한다. 대각선 입력도 정규화된 방향이라
            // 속도가 같다 - 서버의 이동 거리 검증(Game_MoveRequest)이 대각선에서 더 빠르게 보지 않는다.
            // 공격 방향 보조가 방향을 잠근 동안(facingLockUntil)에는 몸을 돌리지 않고 이동만 한다.
            if (Time.time >= facingLockUntil)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                rigidBody.MoveRotation(Quaternion.RotateTowards(rigidBody.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime));
            }

            rigidBody.MovePosition(rigidBody.position + moveDirection * (moveSpeed * Time.fixedDeltaTime));
        }
        #endregion

        #region Method
        /// <summary>
        /// 캐릭터를 worldDirection(수평 성분만 사용) 쪽으로 즉시 돌리고, holdSeconds 동안은 이동 입력으로 몸이 다시 돌아가지 않게 한다.
        /// 공격 방향 보조(PlayerAttackController)가 공격 순간 가까운 몬스터를 향하게 할 때 쓴다. 대쉬 중에는 대쉬 방향이 우선이라
        /// 호출해도 대쉬가 끝난 뒤에야 의미가 있다.
        /// </summary>
        public void FaceDirection(Vector3 worldDirection, float holdSeconds)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f || rigidBody == null)
            {
                return;
            }

            Quaternion facing = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            transform.rotation = facing;
            rigidBody.rotation = facing;
            facingLockUntil = Time.time + Mathf.Max(0f, holdSeconds);
        }

        /// <summary>
        /// 화면 기준 입력(x: 오른쪽, y: 위)을 월드 방향으로 바꾼다. 카메라가 바라보는 방향의 수평 성분을 "위"로 쓰므로
        /// 카메라 각도를 바꿔도 입력이 화면과 맞는다. 입력이 없으면 Vector3.zero.
        /// </summary>
        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (input == Vector2.zero)
            {
                return Vector3.zero;
            }

            Vector3 forward = GetCameraForwardOnPlane();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return (forward * input.y + right * input.x).normalized;
        }

        private Vector3 GetCameraForwardOnPlane()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            Vector3 forward = cameraTransform != null
                ? cameraTransform.forward
                : Quaternion.Euler(0f, fallbackCameraYaw, 0f) * Vector3.forward;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Quaternion.Euler(0f, fallbackCameraYaw, 0f) * Vector3.forward;
            }

            return forward.normalized;
        }

        private void UpdateAnimatorState()
        {
            if (animator == null)
            {
                return;
            }

            bool isMoving = moveDirection != Vector3.zero;
            animator.SetBool(IsMoveHash, isMoving);
            animator.SetBool(IsIdleHash, !isMoving);
            animator.SetBool(IsDashHash, isDashing);
        }

        /// <summary>
        /// Space 입력으로 캐릭터 대쉬를 시작한다(IsDash -> Dash 상태). 누르고 있는 방향키 쪽으로 나가고, 방향키를 누르지 않았으면
        /// 지금 바라보는 방향으로 나간다. 캐릭터는 대쉬 방향으로 즉시 몸을 돌리고 바닥면에 Dash01 이펙트를 재생한다.
        /// dashCooldown이 지나기 전까지는 재입력을 받지 않는다.
        /// </summary>
        private void StartDash()
        {
            Vector3 direction = moveDirection;
            if (direction == Vector3.zero)
            {
                direction = transform.forward;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            }

            dashDirection = direction;

            // 대쉬 모션과 이펙트는 몸이 대쉬 방향을 향한 상태에서 재생되어야 하므로, 이동 방향 회전을 기다리지 않고 바로 맞춘다.
            Quaternion facing = Quaternion.LookRotation(dashDirection, Vector3.up);
            transform.rotation = facing;
            rigidBody.rotation = facing;

            isDashing = true;
            dashEndTime = Time.time + dashDuration;
            nextDashReadyTime = dashEndTime + dashCooldown;
            // dashDuration이 0으로 설정된 경우(즉시 대쉬) 나눗셈을 피한다 - 어차피 다음 프레임에 바로 EndDash된다.
            currentDashSpeed = dashDuration > 0f ? dashDistance / dashDuration : 0f;

            PlayDashEffect();

            // 서버가 무적 구간을 정하도록 알린다. 몬스터 공격 판정은 서버 권위라 이 알림이 있어야 대쉬로 피할 수 있다.
            // 방향이 자유라 후방 대쉬는 따로 없으므로 항상 전방 대쉬 모션으로 알린다.
            GameServerConnectManager.Instance?.SendDash(false);
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
