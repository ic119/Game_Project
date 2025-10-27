using UnityEngine;

namespace JJORY.Controller
{
    public class PlayerController : MonoBehaviour
    {
        #region Variable
        [Header("이동 관련")]
        [SerializeField, Range(0f, 20f)] private float moveSpeed = 6f;
        [SerializeField] private bool isCameraRelative = false; // true면 카메라 기준(평면) 이동

        [Header("중력 및 지형 관련")]
        [SerializeField] private float gravity = 25f;
        [SerializeField, Range(0f, 80f)] private float slopeLimit = 50f; // 허용 경사
        [SerializeField] private float groundCheckDistance = 0.08f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("캐릭터 외형 관련")]
        [SerializeField] private float radius = 0.3f;
        [SerializeField] private float height = 1.8f;
        [SerializeField] private Vector3 center = new Vector3(0, 0.9f, 0);
        [SerializeField, Tooltip("충돌 끼임 방지 여유")] private float skin = 0.02f;
        [SerializeField, Tooltip("충돌해결 반복 횟수")] private int maxSlideIterations = 3;

        private Vector3 velocity;       // 현재 프레임 속도(중력 포함)
        private bool isGrounded;
        private Vector3 groundNormal = Vector3.up;
        #endregion

        #region LifeCycle
        private void Update()
        {
            float dt = Time.deltaTime;

            Vector2 input = GetArrowInput();

            // 1) 화살표 입력 (Horizontal: ←→, Vertical: ↑↓)
            //Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            Vector3 wishDir = Vector3.zero;

            if (isCameraRelative && Camera.main != null)
            {
                var cam = Camera.main.transform;
                Vector3 f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 r = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                wishDir = (f * input.y + r * input.x).normalized;
            }
            else
            {
                wishDir = (Vector3.forward * input.y + Vector3.right * input.x).normalized;
            }

            // 2) 목표 수평 속도 구성
            Vector3 horizontalVel = wishDir * moveSpeed;

            // 3) 중력/접지 처리
            GroundCheck(); // isGrounded, groundNormal 갱신

            if (isGrounded)
            {
                // 접지 시 수직속도 고정(바닥에 밀착)
                if (Vector3.Angle(groundNormal, Vector3.up) <= slopeLimit)
                {
                    velocity.y = -0f; // 살짝 누르는 정도
                }
                else
                {
                    velocity += Vector3.down * gravity * dt; // 가파르면 미끄러질 수 있게 중력 유지
                }
            }
            else
            {
                velocity += Vector3.down * gravity * dt;
            }

            // 4) 최종 속도(수평 + 수직)
            velocity.x = horizontalVel.x;
            velocity.z = horizontalVel.z;

            // 5) 충돌/슬라이드 반영 이동
            Vector3 displacement = velocity * dt;
            MoveWithSlides(displacement);

            // 6) 이동 후 다시 접지 체크(프레임 안정용)
            GroundCheck();
        }
        #endregion

        #region Method
        /// <summary>
        /// 화살표 입력을 통해 이동값 계산
        /// </summary>
        /// <returns></returns>
        private Vector2 GetArrowInput()
        {
            Vector2 input = Vector2.zero;
            if (Input.GetKey(KeyCode.UpArrow))
            {
                input.y += 1f;
            }
            if (Input.GetKey(KeyCode.DownArrow))
            {
                input.y -= 1f;
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                input.x += 1f;
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                input.x -= 1f;
            }

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        #endregion

        /// <summary>
        /// 충돌 및 슬라이드 해결 처리
        /// </summary>
        /// <param name="_displacement"></param>
        private void MoveWithSlides(Vector3 _displacement)
        {
            Vector3 remaining = _displacement;
            Vector3 pos = transform.position;

            for (int i = 0; i < maxSlideIterations; i++)
            {
                if (remaining.sqrMagnitude <= 1e-8f)
                {
                    break;
                }

                Vector3 dir = remaining.normalized;
                float dist = remaining.magnitude;

                if (CapsuleCast(pos, dir, dist + skin, out RaycastHit hit))
                {
                    // 1) 충돌 지점 바로 앞까지 이동
                    float travel = Mathf.Max(hit.distance - skin, 0f);
                    pos += dir * travel;

                    // 2) 남은 이동을 표면 기준으로 슬라이드
                    Vector3 n = hit.normal;
                    remaining = Vector3.ProjectOnPlane(remaining - dir * travel, n);

                    // 3) 너무 작은 노멀은 무시(수치 안정)
                    if (n.sqrMagnitude < 1e-6f)
                    {
                        break;
                    }
                }
                else
                {
                    // 충돌 없으면 남은 만큼 이동
                    pos += remaining;
                    break;
                }
            }
            transform.position = pos;
        }

        /// <summary>
        /// 지형 체크
        /// </summary>
        private void GroundCheck()
        {
            // 캡슐의 아래 끝점에서 살짝 아래로 검사
            GetCapsule(out Vector3 p1, out Vector3 p2);
            Vector3 down = Vector3.down;

            // 스피어캐스트로 바닥 확인
            if (Physics.CapsuleCast(p1, p2, radius - 0.01f, down, out RaycastHit hit, groundCheckDistance + skin, collisionMask, QueryTriggerInteraction.Ignore))
            {
                groundNormal = hit.normal;
                float angle = Vector3.Angle(groundNormal, Vector3.up);
                isGrounded = angle <= 89.9f && hit.distance <= groundCheckDistance + skin + 0.001f;
            }
            else
            {
                isGrounded = false;
                groundNormal = Vector3.up;
            }
        }

        /// <summary>
        /// 캡슐캐스트로 통해 처리
        /// </summary>
        /// <param name="_worldPos"></param>
        /// <param name="_dir"></param>
        /// <param name="_dist"></param>
        /// <param name="_hit"></param>
        /// <returns></returns>
        private bool CapsuleCast(Vector3 _worldPos, Vector3 _dir, float _dist, out RaycastHit _hit)
        {
            GetCapsule(out Vector3 p1, out Vector3 p2, _worldPos);
            return Physics.CapsuleCast(p1, p2, radius, _dir, out _hit, _dist, collisionMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 캡슐 크기 Get
        /// </summary>
        /// <param name="_p1"></param>
        /// <param name="_p2"></param>
        /// <param name="_overridePos"></param>
        private void GetCapsule(out Vector3 _p1, out Vector3 _p2, Vector3? _overridePos = null)
        {
            Vector3 basePos = _overridePos ?? transform.position;
            Vector3 c = basePos + center;
            float half = Mathf.Max(0f, (height * 0.5f) - radius);
            _p1 = c + Vector3.up * half; // 위 구 중심
            _p2 = c - Vector3.up * half; // 아래 구 중심
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            GetCapsule(out var p1, out var p2);
            UnityEditor.Handles.color = new Color(0, 1, 1, 0.5f);
            UnityEditor.Handles.DrawWireDisc(p1, Vector3.up, radius);
            UnityEditor.Handles.DrawWireDisc(p2, Vector3.up, radius);
            Gizmos.color = new Color(0, 1, 1, 0.25f);
            Gizmos.DrawLine(p1 + Vector3.right * radius, p2 + Vector3.right * radius);
            Gizmos.DrawLine(p1 - Vector3.right * radius, p2 - Vector3.right * radius);
            Gizmos.DrawLine(p1 + Vector3.forward * radius, p2 + Vector3.forward * radius);
            Gizmos.DrawLine(p1 - Vector3.forward * radius, p2 - Vector3.forward * radius);
        }
#endif
    }
}