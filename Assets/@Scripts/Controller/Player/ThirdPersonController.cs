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

        [Header("상태 변수")]
        [SerializeField] private bool isGrounded = false;
        [SerializeField] private bool isJumping = false;

        // 내부 변수
        private Vector3 moveDirection = Vector3.zero;
        private Vector3 inputVector = Vector3.zero;
        private float currentSpeed = 0f;
        private float verticalVelocity = 0f;
        private Transform cameraTransform;
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
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
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
            // Raycast를 사용하여 지면 체크
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            RaycastHit hit;

            bool wasGrounded = isGrounded;
            isGrounded = Physics.Raycast(rayOrigin, Vector3.down, out hit, groundCheckDistance + 0.1f, groundLayerMask);

            // 점프 상태 업데이트
            if (wasGrounded && !isGrounded)
            {
                // 땅에서 떨어짐
                isJumping = true;
            }
            else if (!wasGrounded && isGrounded)
            {
                // 땅에 착지
                isJumping = false;
                verticalVelocity = 0f;
            }

            // 추가 체크: Rigidbody의 velocity.y를 확인
            if (rigidbody != null && !rigidbody.isKinematic)
            {
                if (Mathf.Abs(rigidbody.linearVelocity.y) > 0.1f)
                {
                    isJumping = true;
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
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * (groundCheckDistance + 0.1f));
            Gizmos.DrawWireSphere(rayOrigin + Vector3.down * (groundCheckDistance + 0.1f), 0.1f);
        }
        #endregion
    }
}