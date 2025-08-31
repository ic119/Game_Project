using UnityEngine;


namespace JJORY.Module
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("이동 속도")]
        [SerializeField] float walkSpeed = 4.0f;        // 기본 속도
        [SerializeField] float sprintSpeed = 7.0f;      // Shift 가속 속도
        [SerializeField] float acceleration = 12.0f;    // 가감속(가속)
        [SerializeField] float deceleration = 16.0f;    // 가감속(감속)

        [Header("회전")]
        [SerializeField] float rotationLerp = 12.0f;    // 회전 부드러움(지수 보간)
        [Tooltip("카메라 기준으로 WASD 입력을 해석할지 여부")]
        [SerializeField] bool cameraRelativeMove = true;

        [Header("중력")]
        [SerializeField] float gravity = -20.0f;        // 약간 강하게(경사/스텝 안정)
        [SerializeField] float groundedStick = -2.0f;   // 지면에 붙는 느낌

        [Header("참조")]
        [SerializeField] Transform cameraTransform;     // 비워두면 자동 할당

        CharacterController controller;
        float currentSpeed;          // 현재 수평 속력(크기)
        float verticalVelocity;      // y속도(중력)
        Vector3 inputDir;            // 정규화 입력 방향(수평)
        Vector3 moveDirWorld;        // 월드 기준 이동 방향(수평)

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            ReadInput();             // 입력 읽기 (WASD/Shift)
            ComputeMoveDirection();  // 카메라 기준 방향 변환 + 정규화
            UpdateSpeed();           // 가감속으로 속도 부드럽게
            ApplyGravity();          // 중력 처리
            MoveCharacter();         // CharacterController.Move
            RotateTowardsMove();     // 이동 방향으로 자연 회전
        }

        // 1) 입력
        void ReadInput()
        {
            float h = Input.GetAxisRaw("Horizontal"); // A/D
            float v = Input.GetAxisRaw("Vertical");   // W/S
            Vector3 raw = new Vector3(h, 0f, v);
            inputDir = raw.sqrMagnitude > 1f ? raw.normalized : raw; // 대각 정규화
        }

        // 2) 이동 방향 계산(카메라 기준 옵션)
        void ComputeMoveDirection()
        {
            if (cameraRelativeMove && cameraTransform != null)
            {
                Vector3 camForward = cameraTransform.forward;
                Vector3 camRight = cameraTransform.right;
                camForward.y = 0f; camRight.y = 0f;
                camForward.Normalize(); camRight.Normalize();

                moveDirWorld = camForward * inputDir.z + camRight * inputDir.x;
            }
            else
            {
                moveDirWorld = new Vector3(inputDir.x, 0f, inputDir.z);
            }

            if (moveDirWorld.sqrMagnitude > 1f) moveDirWorld.Normalize();
        }

        // 3) 속도 가감속 (Shift로 타겟 속도 변경)
        void UpdateSpeed()
        {
            float targetSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
            float accel = (targetSpeed > currentSpeed) ? acceleration : deceleration;

            // 입력 없으면 타겟 속도를 0으로 해서 자연 감속
            if (moveDirWorld.sqrMagnitude < 0.0001f) targetSpeed = 0f;

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * Time.deltaTime);
        }

        // 4) 중력
        void ApplyGravity()
        {
            if (controller.isGrounded)
            {
                // 지면에 닿아있을 때 약간 아래로 잡아당겨 '붙는' 느낌
                verticalVelocity = groundedStick;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }

        // 5) 이동
        void MoveCharacter()
        {
            Vector3 horizontal = moveDirWorld * currentSpeed;   // 수평 이동
            Vector3 velocity = horizontal + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }

        // 6) 회전 (이동 중에만 목표 방향으로 부드럽게)
        void RotateTowardsMove()
        {
            Vector3 look = moveDirWorld;
            if (look.sqrMagnitude < 0.0001f) return;

            Quaternion targetRot = Quaternion.LookRotation(look, Vector3.up);
            // 지수 보간(프레임율 무관)
            float t = 1f - Mathf.Exp(-rotationLerp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }
    }
}