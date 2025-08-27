using UnityEngine;


namespace JJORY.Module
{
    public class CharacterController : MonoBehaviour
    {
        #region Variable
        [Header("이동 관련")]
        [SerializeField] private float move_Speed = 5.0f;          // 평면 이동 속도 (m/s)
        [SerializeField] private float accel = 20.0f;              // 가속 (더 부드러운 가감속)
        [SerializeField] private float max_SlopeAngle = 50.0f;      // 오르내릴 수 있는 경사 제한 각도
        [SerializeField] private bool camera_Relative = true;     // 카메라 기준 WASD 여부

        [Header("회전 관련")]
        [SerializeField] private float rotate_Lerp = 15.0f;

        [Header("캐릭터 오브젝트 속성")]
        [SerializeField] private Rigidbody rigidbody;
        [SerializeField] private Vector3 input_Dir;
        [SerializeField] private bool isGround;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            rigidbody = GetComponent<Rigidbody>();
            rigidbody.freezeRotation = true; // X/Z 흔들림 방지 (회전은 MoveRotation으로)
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private void Update()
        {
            Move();
        }

        private void FixedUpdate()
        {
            CollisionCheck();
        }

        private void OnCollisionStay(Collision collision)
        {
            foreach (var c in collision.contacts)
            {
                if (Vector3.Angle(c.normal, Vector3.up) < 60f)
                {
                    isGround = true;
                    return;
                }
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            // 충돌이 모두 사라지면 접지 해제될 수 있으니 지연 해제 대신 바로 false
            isGround = false;
        }
        #endregion

        #region Method
        private void Move()
        {
            float x = Input.GetAxisRaw("Horizontal"); 
            float z = Input.GetAxisRaw("Vertical");   
            Vector3 raw = new Vector3(x, 0f, z);
            input_Dir = raw.sqrMagnitude > 1f ? raw.normalized : raw;

            //if (isGround && Input.GetKeyDown(KeyCode.Space))
            //{
            //    // 점프 요청은 FixedUpdate에서 실제 힘을 가함
            //    pendingJump = true;
            //}
        }

        private void CollisionCheck()
        {
            if (rigidbody == null)
            {
                return;
            }

            // 1) 이동 방향을 월드/카메라 기준으로 변환
            Vector3 moveDir = input_Dir;
            if (camera_Relative && Camera.main != null)
            {
                Vector3 camFwd = Camera.main.transform.forward;
                Vector3 camRight = Camera.main.transform.right;
                camFwd.y = 0f; camRight.y = 0f;
                camFwd.Normalize(); camRight.Normalize();
                moveDir = camRight * input_Dir.x + camFwd * input_Dir.z;
            }

            // 2) 지면 경사에 투영 (경사 제한)
            Vector3 desired = moveDir * move_Speed;
            if (isGround && GetGroundNormal(out Vector3 groundNormal))
            {
                // 너무 가파르면 이동 억제
                float slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
                if (slopeAngle <= max_SlopeAngle)
                {
                    // 경사면 위로 미끄러지지 않도록 평면 투영
                    desired = Vector3.ProjectOnPlane(desired, groundNormal);
                }
                else
                {
                    // 가파른 곳에서는 상쇄
                    desired = Vector3.zero;
                }
            }

            // 3) 가감속(부드럽게 속도 맞추기)
            Vector3 current = rigidbody.linearVelocity;
            Vector3 target = new Vector3(desired.x, current.y, desired.z);
            rigidbody.linearVelocity = Vector3.MoveTowards(current, target, accel * Time.fixedDeltaTime);

            // 4) 점프 처리
            //if (enableJump && pendingJump && isGrounded)
            //{
            //    rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            //    rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            //}
            //pendingJump = false;

            // 5) 이동 방향을 바라보는 회전 (옵션)
            Vector3 flatVel = new Vector3(rigidbody.linearVelocity.x, 0f, rigidbody.linearVelocity.z);
            if (flatVel.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(flatVel);
                rigidbody.MoveRotation(Quaternion.Slerp(rigidbody.rotation, look, rotate_Lerp * Time.fixedDeltaTime));
            }
        }

        private bool GetGroundNormal(out Vector3 normal)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 0.5f, ~0))
            {
                normal = hit.normal;
                return true;
            }
            normal = Vector3.up;
            return false;
        }
        #endregion
    }
}