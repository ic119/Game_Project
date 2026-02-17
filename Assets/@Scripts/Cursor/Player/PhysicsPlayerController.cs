using JJORY.Controller.Camera;
using JJORY.Controller.Player;
using JJORY.Util;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Rigidbody))]
public class PhysicsPlayerController : MonoBehaviour
{
    #region Variable
    [Header("Components")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Rigidbody rigidbodyComponent;

    [Header("Movement")]
    [SerializeField, Range(0.5f, 10f)] private float moveSpeed = 3.5f;
    [SerializeField, Range(0f, 1080f)] private float rotateSpeed = 540f;
    [SerializeField, Range(1f, 100f)] private float groundAcceleration = 40f;

    [Header("Jump")]
    [SerializeField, Range(0.5f, 5f)] private float jumpHeight = 5.2f;
    [SerializeField, Range(0f, 2f)] private float jumpForwardMultiplier = 1.0f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.5f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -19.62f;
    [SerializeField] private float groundedSnapSpeed = -2.0f;
    private float verticalVelocity;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private float groundCheckDistance = 0.3f;

    [Header("Wall Check")]
    [SerializeField] private LayerMask wallMask = ~0;
    [SerializeField] private float wallCheckRadius = 0.3f;
    [SerializeField] private float wallCheckDistance = 0.5f;

    private Vector3 lastMoveDirection;
    private Vector3 lastWallNormal;
    private Vector3 currentHorizontalVelocity;

    [Header("Animator 관련")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerAnimationController animationController;

    [Header("카메라 관련")]
    [SerializeField] private CameraFollowController cameraFollowController;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
        if (rigidbodyComponent == null)
        {
            rigidbodyComponent = GetComponent<Rigidbody>();
        }
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
        if (animationController == null)
        {
            animationController = GetComponent<PlayerAnimationController>();
        }
        if (cameraFollowController == null)
        {
            cameraFollowController = GameObject.FindFirstObjectByType<CameraFollowController>();
        }

        cameraFollowController.SetTarget(gameObject.transform);

        // Rigidbody는 물리 시뮬레이션 충돌 감지 용도로만 사용하고 이동은 CharacterController로 수행
        rigidbodyComponent.isKinematic = true;
        rigidbodyComponent.useGravity = false;
        rigidbodyComponent.constraints = RigidbodyConstraints.FreezeRotation;

        currentHorizontalVelocity = Vector3.zero;
    }

    private void Update()
    {
        ProcessMovement();
    }
    #endregion

    #region Method
    /// <summary>
    /// 이동 관련 처리
    /// </summary>
    private void ProcessMovement()
    {
        bool isJumpThisFrame = false;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v);
        input = Vector3.ClampMagnitude(input, 1f);

        // 달리기(Shift) 입력: 이동 속도를 2.5배로
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float currentMoveSpeed = moveSpeed * (isSprinting ? 1.5f : 1f);

        // 월드 기준 이동 방향 (필요 시 카메라 기준으로 변환 가능)
        Vector3 moveDir = input;
        bool hasInput = moveDir.sqrMagnitude > 0.0001f;
        if (hasInput)
        {
            // Y 성분 제거 및 정규화
            moveDir = new Vector3(moveDir.x, 0f, moveDir.z).normalized;
            lastMoveDirection = moveDir;

            // 회전
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
        }

        // Ground 체크
        bool isGrounded = IsGrounded();

        // 점프 입력
        bool jumpPressed = Input.GetKeyDown(KeyCode.Space);

        // 회전 중 슬라이딩 방지: 회전이 거의 완료될 때만 이동 허용
        bool canMove = true;
        if (hasInput && isGrounded)
        {
            // 현재 바라보는 방향과 목표 이동 방향의 각도 차이 계산
            float angleDifference = Vector3.Angle(transform.forward, moveDir);
            // 회전 각도 차이가 5도 이하일 때만 이동 허용
            canMove = angleDifference <= 5f;
        }

        // 수평 속도 계산 (지상/공중 구분)
        Vector3 desiredHorizontal = (hasInput && canMove) ? moveDir * currentMoveSpeed : Vector3.zero;
        if (isGrounded)
        {
            // 착지 처리
            if (verticalVelocity < 0f)
            {
                // 지면 위에서 살짝 붙게 유지 (CharacterController 특성상 0보다 작은 값 권장)
                verticalVelocity = groundedSnapSpeed;
            }

            // 지상에서는 가속/감속을 주어 부드럽게 속도가 변하도록 처리
            Vector3 targetHorizontal = desiredHorizontal;
            currentHorizontalVelocity = Vector3.MoveTowards(
                currentHorizontalVelocity,
                targetHorizontal,
                groundAcceleration * Time.deltaTime
            );

            // 지상 점프
            if (jumpPressed)
            {
                float initialVel = CalculateJumpVelocity(jumpHeight);
                verticalVelocity = initialVel;
                isJumpThisFrame = true;
                animationController.SetMoveState(PlayerMoveState.Jump);

                // 포물선 진행을 위한 초기 수평 추진 (입력 없을 때는 마지막 바라보는 방향 사용)
                Vector3 forwardBasis = (moveDir.sqrMagnitude > 0.0001f)
                    ? moveDir
                    : (lastMoveDirection.sqrMagnitude > 0.0001f ? lastMoveDirection : transform.forward);
                if (jumpForwardMultiplier > 0f)
                {
                    // 달리기 중이라면 더 빠른 수평 속도로 점프
                    Vector3 jumpHorizontal = forwardBasis.normalized * (currentMoveSpeed * jumpForwardMultiplier);
                    // 점프 순간에는 기존 속도와 합성하되, 입력이 약하면 보강
                    currentHorizontalVelocity = (desiredHorizontal.sqrMagnitude > 0.0001f)
                        ? Vector3.Max(currentHorizontalVelocity, jumpHorizontal)
                        : jumpHorizontal;
                }
            }
        }
        else
        {
            // 공중에서는 중력 적용
            verticalVelocity += gravity * Time.deltaTime;

            // 공중 조작: 일정 비율로 원하는 속도에 수렴
            if (desiredHorizontal.sqrMagnitude > 0.0001f)
            {
                currentHorizontalVelocity = Vector3.Lerp(currentHorizontalVelocity, desiredHorizontal, airControl * Time.deltaTime);
            }

        }

        // 최종 이동 벡터
        Vector3 horizontal = currentHorizontalVelocity;
        Vector3 velocity = new Vector3(horizontal.x, verticalVelocity, horizontal.z);

        // 이동
        CollisionFlags flags = characterController.Move(velocity * Time.deltaTime);

        // 벽 충돌 체크 (CharacterController 기준)
        bool touchWallByFlags = (flags & CollisionFlags.Sides) != 0;
        bool touchWallByCast = CheckWallWithSphereCast(moveDir);
        bool isTouchingWall = touchWallByFlags || touchWallByCast;

        // 머리 부딪힘 처리: 위쪽 충돌 시 상승 속도 제거
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }

        // 필요 시 로깅 (과다 로그 방지를 위해 조건부 사용 권장)
        if (isTouchingWall)
        {
            Utils.CreateLogError<PhysicsPlayerController>("벽 충돌 감지");
        }

        // 애니메이션 상태 갱신 (점프 애니메이션이 우선)
        if (!isJumpThisFrame)
        {
            if (isGrounded)
            {
                if (!hasInput)
                {
                    animationController.SetMoveState(PlayerMoveState.Idle);
                }
                else
                {
                    if (isSprinting)
                    {
                        animationController.SetMoveState(PlayerMoveState.Run);
                    }
                    else
                    {
                        animationController.SetMoveState(PlayerMoveState.Walk);
                    }
                }
            }
            // 공중에서는 직전에 설정된 점프/이동 상태를 유지
        }
    }

    /// <summary>
    /// 점프 시 속도 계산 처리
    /// </summary>
    /// <param name="height"></param>
    /// <returns></returns>
    private float CalculateJumpVelocity(float height)
    {
        // gravity는 음수 가정
        height = Mathf.Max(0.0001f, height);
        return Mathf.Sqrt(-2f * gravity * height);
    }

    private bool IsGrounded()
    {
        if (characterController.isGrounded)
        {
            return true;
        }

        // CharacterController의 바닥 근처에서 보조 체크 (SphereCast)
        Vector3 origin = GetGroundCheckOrigin();
        if (Physics.SphereCast(origin, groundCheckRadius, Vector3.down, out _, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return false;
    }

    private Vector3 GetGroundCheckOrigin()
    {
        // CharacterController bounds를 사용해 바닥 부근에서 캐스트
        Bounds b = characterController.bounds;
        Vector3 origin = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
        return origin;
    }

    private bool CheckWallWithSphereCast(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * (characterController.height * 0.5f);
        if (Physics.SphereCast(origin, wallCheckRadius, moveDir.normalized, out RaycastHit hit, wallCheckDistance, wallMask, QueryTriggerInteraction.Ignore))
        {
            lastWallNormal = hit.normal;
            return true;
        }
        return false;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // CharacterController가 벽/장애물과 부딪힐 때 호출됨
        // 마지막 벽 노멀 저장 (필요 시 반사/슬라이딩 처리에 활용)
        if (hit.moveDirection.y <= 0.1f)
        {
            lastWallNormal = hit.normal;
        }
    }

    // 외부 조회용 보조 API
    public Vector3 GetLastWallNormal() => lastWallNormal;
    public Vector3 GetLastMoveDirection() => lastMoveDirection;
    #endregion
}