using Incheol.Utils;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Incheol.Controller
{
    /// <summary>
    /// 마우스 우클릭 드래그로 카메라를 캐릭터 둘레로 수평 회전시킨다. 카메라는 월드 기준 오프셋(CinemachineFollow)으로 캐릭터를
    /// 따라가므로, 그 오프셋을 수평(Y축)으로 돌려 시점을 바꾼다 - 고정 각도로는 가려 보이지 않는 구간(벽 뒤 등)을 볼 수 있다.
    /// 높이/거리와 위에서 내려다보는 각도는 그대로고 수평 방향만 바뀐다. 캐릭터 이동 입력은 PlayerMoveController가 실제 카메라
    /// 방향(Camera.main)을 읽어 해석하므로 회전한 시점에 맞춰 자동으로 따라간다.
    /// </summary>
    [RequireComponent(typeof(CinemachineFollow))]
    public class CameraOrbitController : MonoBehaviour
    {
        #region Variable
        [Header("Input")]
        [Tooltip("끄면 마우스 드래그로 카메라가 돌지 않는다(탑다운 고정 시점). 피격 흔들림은 이 값과 상관없이 동작한다. 맵 프리팹의 MapCameraProfile이 맵마다 덮어쓴다.")]
        [SerializeField] private bool enableRotation = true;

        [Tooltip("드래그로 카메라를 돌리는 마우스 버튼(0 = 좌클릭, 1 = 우클릭, 2 = 휠 클릭).")]
        [SerializeField, Range(0, 2)] private int dragMouseButton = 1;

        [Tooltip("마우스를 좌우로 움직일 때 카메라가 도는 속도. Input Manager의 Mouse X 값 1당 도는 각도(도)다.")]
        [SerializeField, Min(0f)] private float rotateSensitivity = 4f;

        [Tooltip("켜면 마우스를 오른쪽으로 움직일 때 카메라가 반대 방향으로 돈다.")]
        [SerializeField] private bool invertHorizontal;

        [Tooltip("드래그하는 동안 커서를 화면 중앙에 고정하고 숨긴다(창 밖으로 커서가 나가 드래그가 끊기는 것을 막는다).")]
        [SerializeField] private bool lockCursorWhileDragging = true;

        private CinemachineFollow follow;

        // 씬에 설정된 기본 오프셋(수평 45도 시점). 이 값을 yawOffset만큼 돌려 쓴다.
        private Vector3 baseOffset;
        private float yawOffset;
        private bool isDragging;

        // 흔들림 상태. 오프셋을 쓰는 곳이 이 컴포넌트 하나뿐이라 회전과 흔들림을 여기서 합쳐 적용한다(따로 두면 서로 덮어쓴다).
        private bool isShaking;
        private float shakeStartTime;
        private float shakeDuration;
        private float shakeAmplitude;
        private Vector3 shakeOffset;

        /// <summary>씬에 이 컴포넌트가 활성화돼 있는 동안만 값이 있다. 호출부는 ?.로 부른다.</summary>
        public static CameraOrbitController Instance { get; private set; }
        #endregion

        #region LifeCycle
        private void Awake()
        {
            follow = GetComponent<CinemachineFollow>();
            baseOffset = follow.FollowOffset;
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void Update()
        {
            UpdateShake();

            // 채팅 입력 중에는 마우스 입력으로 카메라가 돌지 않게 한다(다른 게임플레이 입력과 같은 InputBlocker).
            if (!enableRotation || InputBlocker.IsBlocked)
            {
                StopDragging();
                return;
            }

            if (!isDragging && Input.GetMouseButtonDown(dragMouseButton) && !IsPointerOverUI())
            {
                StartDragging();
            }

            if (!isDragging)
            {
                return;
            }

            if (!Input.GetMouseButton(dragMouseButton))
            {
                StopDragging();
                return;
            }

            float mouseX = Input.GetAxis("Mouse X");
            if (mouseX != 0f)
            {
                yawOffset += (invertHorizontal ? -mouseX : mouseX) * rotateSensitivity;
                ApplyOffset();
            }
        }

        // 드래그 도중 창 포커스를 잃거나 오브젝트가 꺼져도 커서가 잠긴 채 남지 않게 한다.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                StopDragging();
            }
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            isShaking = false;
            StopDragging();
        }
        #endregion

        #region Method
        /// <summary>
        /// 맵이 바뀔 때(MapCameraProfile) 호출된다. 기준 오프셋을 새 값으로 바꾸고 회전을 초기화하며, 회전 허용 여부를 맞춘다.
        /// 오프셋이 바뀌면 CinemachineFollow의 PositionDamping에 따라 카메라가 새 시점으로 부드럽게 이동한다.
        /// </summary>
        public void ApplyProfile(Vector3 offset, bool allowRotation)
        {
            baseOffset = offset;
            yawOffset = 0f;
            enableRotation = allowRotation;

            if (!enableRotation)
            {
                StopDragging();
            }

            ApplyOffset();
        }

        /// <summary>회전을 기본 시점(씬에 설정된 수평 각도)으로 되돌린다.</summary>
        public void ResetRotation()
        {
            yawOffset = 0f;
            ApplyOffset();
        }

        /// <summary>
        /// 카메라를 짧게 흔든다(피격 연출 등). amplitude는 흔들림 크기(m), duration은 지속 시간(초)이며 끝으로 갈수록 잦아든다.
        /// 이미 흔들리는 중이면 더 큰 쪽으로 이어받는다 - 연속 피격에 흔들림이 계속 이어진다.
        /// </summary>
        public void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            shakeAmplitude = isShaking ? Mathf.Max(amplitude, shakeAmplitude * RemainingShakeRatio()) : amplitude;
            shakeDuration = duration;
            shakeStartTime = Time.time;
            isShaking = true;
        }

        private float RemainingShakeRatio()
        {
            return shakeDuration > 0f ? Mathf.Clamp01(1f - (Time.time - shakeStartTime) / shakeDuration) : 0f;
        }

        private void UpdateShake()
        {
            if (!isShaking)
            {
                return;
            }

            float elapsed = Time.time - shakeStartTime;
            if (elapsed >= shakeDuration)
            {
                isShaking = false;
                shakeOffset = Vector3.zero;
                ApplyOffset();
                return;
            }

            shakeOffset = Random.insideUnitSphere * (shakeAmplitude * (1f - elapsed / shakeDuration));
            ApplyOffset();
        }

        private void ApplyOffset()
        {
            follow.FollowOffset = Quaternion.Euler(0f, yawOffset, 0f) * baseOffset + (isShaking ? shakeOffset : Vector3.zero);
        }

        private void StartDragging()
        {
            isDragging = true;

            if (lockCursorWhileDragging)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        private void StopDragging()
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;

            if (lockCursorWhileDragging)
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // 인벤토리 같은 UI 위에서 시작한 우클릭은 카메라를 돌리지 않는다.
        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
        #endregion
    }
}
