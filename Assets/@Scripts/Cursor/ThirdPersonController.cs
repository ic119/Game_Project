using System;
using UnityEngine;
using JJORY.Util;

namespace JJORY.Controller
{
    [Serializable]
    public class CharacterMoveOptions
    {
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float sprintMultiplier = 1.5f;
        [SerializeField] private bool rotateToMove = true;
        [SerializeField] private float rotationSharpness = 12f;

        public float MoveSpeed => moveSpeed;
        public float SprintMultiplier => sprintMultiplier;
        public bool RotateToMove => rotateToMove;
        public float RotationSharpness => rotationSharpness;
    }

    [Serializable]
    public class CharacterGravityOptions
    {
        [SerializeField] private float gravityPower = -20f;
        [SerializeField] private float groundedStick = -2f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private bool enableJump = true;

        public float GravityPower => gravityPower;
        public float GroundStick => groundedStick;
        public float JumpHeight => jumpHeight;
        public bool EnableJump => enableJump;
    }

    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Rigidbody))]
    public class ThirdPersonController : MonoBehaviour
    {
        #region Variable
        [Header("컴포넌트")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Rigidbody rigidbody;

        [Header("이동 설정")]
        [SerializeField] private CharacterMoveOptions moveOptions = new CharacterMoveOptions();

        [Header("중력 및 점프 설정")]
        [SerializeField] private CharacterGravityOptions gravityOptions = new CharacterGravityOptions();

        [Header("지면 체크 설정")]
        [SerializeField] private float groundCheckDistance = 0.1f;
        [SerializeField] private LayerMask groundLayerMask = 1;
        [SerializeField] private float groundCheckRadius = 0.3f;

        [Header("상태 변수")]
        [SerializeField] private bool isGrounded = false;
        [SerializeField] private bool isJumping = false;

        // 내부 변수
        private Vector3 moveDirection = Vector3.zero;
        private Vector3 inputVector = Vector3.zero;
        private float currentSpeed = 0f;
        private float verticalVelocity = 0f;
        private Transform cameraTransform;
        private int groundContactCount = 0;
        private float lastGroundedTime = 0f;
        #endregion

        #region Properties
        /// <summary>
        /// 캐릭터가 땅에 있는지 여부 (Rigidbody 기반)
        /// </summary>
        public bool IsGrounded => isGrounded;

        /// <summary>
        /// 캐릭터가 점프 중인지 여부
        /// </summary>
        public bool IsJumping => isJumping;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (rigidbody == null)
            {
                rigidbody = GetComponent<Rigidbody>();
            }

            // Rigidbody 설정: CharacterController와 함께 사용하기 위해 Kinematic으로 설정
            // 하지만 지면 감지를 위해 Collision 이벤트는 활성화
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            
            // Collision 감지를 위해 Collider가 필요함
            if (GetComponent<Collider>() == null)
            {
                Debug.LogWarning($"[{GetType().Name}] 지면 감지를 위해 Collider 컴포넌트가 필요합니다.");
            }
        }

        private void Start()
        {
            // 메인 카메라 참조
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            CheckGrounded();
            HandleInput();
            Move();
            ApplyGravity();
        }
        #endregion

        #region Method
        /// <summary>
        /// Rigidbody를 사용하여 지면 체크
        /// </summary>
        private void CheckGrounded()
        {
            bool wasGrounded = isGrounded;
            
            // 방법 1: Rigidbody의 Collision 이벤트를 통한 지면 감지 (주요 방법)
            // groundContactCount는 OnCollisionStay/Enter에서 업데이트됨
            bool collisionBasedGrounded = groundContactCount > 0;
            
            // 방법 2: SphereCast를 사용한 지면 체크 (보조 방법)
            Vector3 sphereOrigin = transform.position + Vector3.up * 0.1f;
            bool sphereCastGrounded = Physics.CheckSphere(
                sphereOrigin + Vector3.down * groundCheckDistance, 
                groundCheckRadius, 
                groundLayerMask
            );
            
            // 방법 3: Raycast를 사용한 지면 체크 (추가 검증)
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            RaycastHit hit;
            bool raycastGrounded = Physics.Raycast(
                rayOrigin, 
                Vector3.down, 
                out hit, 
                groundCheckDistance + 0.1f, 
                groundLayerMask
            );
            
            // Rigidbody의 Collision 기반 감지가 우선순위가 높음
            // 하지만 Collision이 없을 경우를 대비해 다른 방법도 사용
            isGrounded = collisionBasedGrounded || (sphereCastGrounded && raycastGrounded);
            
            // 지면에서 떨어졌을 때 시간 기록
            if (wasGrounded && !isGrounded)
            {
                lastGroundedTime = Time.time;
            }
            
            // 일정 시간 동안 지면에 닿지 않으면 점프 상태로 전환
            if (isGrounded)
            {
                isJumping = false;
                if (!wasGrounded)
                {
                    // 땅에 착지
                    verticalVelocity = 0f;
                }
            }
            else
            {
                // 공중에 있을 때, 짧은 시간 동안은 여전히 지면에 있는 것으로 간주 (Coyote Time)
                float timeSinceGrounded = Time.time - lastGroundedTime;
                if (timeSinceGrounded > 0.1f)
                {
                    isJumping = true;
                }
            }
        }
        
        /// <summary>
        /// Rigidbody Collision 이벤트 - 지면과의 접촉 감지
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            CheckGroundContact(collision, true);
        }
        
        /// <summary>
        /// Rigidbody Collision 이벤트 - 지면과의 접촉 유지 감지
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            CheckGroundContact(collision, true);
        }
        
        /// <summary>
        /// Rigidbody Collision 이벤트 - 지면과의 접촉 해제 감지
        /// </summary>
        private void OnCollisionExit(Collision collision)
        {
            CheckGroundContact(collision, false);
        }
        
        /// <summary>
        /// 지면 접촉 체크
        /// </summary>
        private void CheckGroundContact(Collision collision, bool isEntering)
        {
            // 충돌한 오브젝트가 지면 레이어인지 확인
            int collisionLayer = collision.gameObject.layer;
            if ((groundLayerMask.value & (1 << collisionLayer)) == 0)
            {
                return;
            }
            
            // 충돌 지점이 아래쪽에 있는지 확인
            foreach (ContactPoint contact in collision.contacts)
            {
                Vector3 contactDirection = (contact.point - transform.position).normalized;
                float angle = Vector3.Angle(contactDirection, Vector3.down);
                
                // 접촉 지점이 대략 아래쪽(45도 이내)에 있으면 지면으로 간주
                if (angle < 45f)
                {
                    if (isEntering)
                    {
                        groundContactCount++;
                    }
                    else
                    {
                        groundContactCount = Mathf.Max(0, groundContactCount - 1);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 입력 처리 (WASD 키)
        /// </summary>
        private void HandleInput()
        {
            float horizontal = 0f;
            float vertical = 0f;

            // WASD 키 입력
            if (Input.GetKey(KeyCode.W)) vertical = 1f;
            if (Input.GetKey(KeyCode.S)) vertical = -1f;
            if (Input.GetKey(KeyCode.A)) horizontal = -1f;
            if (Input.GetKey(KeyCode.D)) horizontal = 1f;

            inputVector = new Vector3(horizontal, 0f, vertical).normalized;

            // 점프 입력 처리
            if (Input.GetKeyDown(KeyCode.Space) && isGrounded && !isJumping && gravityOptions.EnableJump)
            {
                Jump();
            }
        }

        /// <summary>
        /// 캐릭터 이동 처리
        /// </summary>
        private void Move()
        {
            if (inputVector.magnitude < 0.1f)
            {
                currentSpeed = Mathf.Lerp(currentSpeed, 0f, Time.deltaTime * moveOptions.RotationSharpness);
                return;
            }

            // 카메라 방향 기준으로 이동 방향 계산
            Vector3 cameraForward = cameraTransform != null ? cameraTransform.forward : transform.forward;
            cameraForward.y = 0f;
            cameraForward.Normalize();

            Vector3 cameraRight = cameraTransform != null ? cameraTransform.right : transform.right;
            cameraRight.y = 0f;
            cameraRight.Normalize();

            // 이동 방향 계산
            moveDirection = (cameraForward * inputVector.z + cameraRight * inputVector.x).normalized;

            // 속도 계산
            float targetSpeed = moveOptions.MoveSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
            {
                targetSpeed *= moveOptions.SprintMultiplier;
            }

            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * moveOptions.RotationSharpness);

            // 캐릭터 회전
            if (moveOptions.RotateToMove && moveDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * moveOptions.RotationSharpness);
            }

            // CharacterController로 이동
            Vector3 moveVector = moveDirection * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;
            characterController.Move(moveVector);
        }

        /// <summary>
        /// 중력 적용
        /// </summary>
        private void ApplyGravity()
        {
            if (isGrounded && !isJumping)
            {
                // 땅에 있을 때는 약간의 힘을 가하여 지면에 붙어있도록 함
                verticalVelocity = gravityOptions.GroundStick;
            }
            else
            {
                // 공중에 있을 때는 중력 적용
                verticalVelocity += gravityOptions.GravityPower * Time.deltaTime;
                verticalVelocity = Mathf.Clamp(verticalVelocity, -50f, 50f);
            }
        }

        /// <summary>
        /// 점프 처리
        /// </summary>
        private void Jump()
        {
            if (!isGrounded || isJumping) return;

            verticalVelocity = Mathf.Sqrt(gravityOptions.JumpHeight * -2f * gravityOptions.GravityPower);
            isJumping = true;
            isGrounded = false;

            Utils.CreateLogMessage<ThirdPersonController>("점프!");
        }
        #endregion

        #region Gizmos
        private void OnDrawGizmosSelected()
        {
            // 지면 체크 시각화
            Vector3 sphereOrigin = transform.position + Vector3.up * 0.1f;
            Vector3 checkPosition = sphereOrigin + Vector3.down * groundCheckDistance;
            
            // Raycast 시각화
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(sphereOrigin, checkPosition);
            
            // SphereCast 시각화
            Gizmos.color = isGrounded ? new Color(0f, 1f, 0f, 0.3f) : new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(checkPosition, groundCheckRadius);
            
            // 지면 접촉 카운트 표시
            if (Application.isPlaying)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.2f);
            }
        }
        #endregion
    }
}