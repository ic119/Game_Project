using UnityEngine;
using Unity.Cinemachine;
using JJORY.Util;
using System.Reflection;

namespace JJORY.Controller
{
    /// <summary>
    /// Unity 6 Cinemachine을 사용한 3인칭 카메라 컨트롤러
    /// 마우스 우클릭 드래그로 회전, 마우스 휠로 줌
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class ThirdPersonCameraController : MonoBehaviour
    {
        #region Variable
        [Header("Cinemachine Camera")]
        [SerializeField] private CinemachineCamera cinemachineCamera;

        [Header("카메라 회전 설정")]
        [SerializeField] private float mouseSensitivity = 2.0f;
        [SerializeField] private float rotationSmoothness = 10.0f;
        [SerializeField] private float minVerticalAngle = -30f;
        [SerializeField] private float maxVerticalAngle = 60f;

        [Header("카메라 줌 설정")]
        [SerializeField] private float zoomSpeed = 2.0f;
        [SerializeField] private float minZoomDistance = 2.0f;
        [SerializeField] private float maxZoomDistance = 10.0f;
        [SerializeField] private float defaultZoomDistance = 5.0f;

        [Header("Follow Target (카메라가 따라갈 타겟)")]
        [SerializeField] private Transform followTarget;

        [Header("Camera Target (회전을 제어할 빈 오브젝트)")]
        [SerializeField] private Transform cameraTarget;

        // 내부 변수
        private float currentHorizontalAngle = 0f;
        private float currentVerticalAngle = 20f;
        private float currentZoomDistance = 5.0f;
        private bool isRightMouseButtonDown = false;
        private Component thirdPersonFollowComponent;
        private FieldInfo distanceField;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (cinemachineCamera == null)
            {
                cinemachineCamera = GetComponent<CinemachineCamera>();
            }

            currentZoomDistance = defaultZoomDistance;

            // ThirdPersonFollow 컴포넌트 찾기
            FindThirdPersonFollowComponent();
        }

        private void Start()
        {
            // Follow Target이 설정되지 않은 경우 자동으로 찾기
            if (followTarget == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    followTarget = player.transform;
                }
            }

            // Camera Target이 없으면 생성
            if (cameraTarget == null)
            {
                GameObject targetObj = new GameObject("CameraTarget");
                targetObj.transform.SetParent(followTarget);
                targetObj.transform.localPosition = Vector3.zero;
                cameraTarget = targetObj.transform;
            }

            // 초기 회전 설정
            if (cameraTarget != null)
            {
                currentHorizontalAngle = cameraTarget.eulerAngles.y;
                currentVerticalAngle = cameraTarget.eulerAngles.x;
            }

            SetupCinemachineCamera();
        }

        private void Update()
        {
            HandleMouseInput();
            UpdateCameraRotation();
            UpdateCameraZoom();
        }
        #endregion

        #region Method
        /// <summary>
        /// ThirdPersonFollow 컴포넌트 찾기 (Cinemachine 3.0)
        /// </summary>
        private void FindThirdPersonFollowComponent()
        {
            if (cinemachineCamera == null) return;

            // Cinemachine 3.0의 CinemachineCamera는 여러 컴포넌트를 가질 수 있음
            // CinemachineThirdPersonFollow 또는 유사한 컴포넌트를 찾음
            Component[] components = cinemachineCamera.GetComponents<Component>();
            
            // 우선순위: ThirdPersonFollow 관련 컴포넌트 먼저 찾기
            foreach (var comp in components)
            {
                string typeName = comp.GetType().Name;
                string fullTypeName = comp.GetType().FullName;
                
                // Cinemachine 3.0의 ThirdPersonFollow 컴포넌트 찾기
                if (fullTypeName.Contains("Cinemachine") && 
                    (typeName.Contains("ThirdPerson") || typeName.Contains("ThirdPersonFollow")))
                {
                    thirdPersonFollowComponent = comp;
                    Utils.CreateLogMessage<ThirdPersonCameraController>($"Found Cinemachine component: {typeName}");

                    // Distance 필드 찾기
                    FindDistanceField(comp.GetType());
                    return;
                }
            }
            
            // ThirdPersonFollow를 찾지 못한 경우, Follow 관련 컴포넌트 찾기
            foreach (var comp in components)
            {
                string typeName = comp.GetType().Name;
                string fullTypeName = comp.GetType().FullName;
                
                if (fullTypeName.Contains("Cinemachine") && typeName.Contains("Follow"))
                {
                    thirdPersonFollowComponent = comp;
                    Utils.CreateLogMessage<ThirdPersonCameraController>($"Found Follow component: {typeName}");

                    // Distance 필드 찾기
                    FindDistanceField(comp.GetType());
                    return;
                }
            }
            
            Utils.CreateLogMessage<ThirdPersonCameraController>("Cinemachine ThirdPersonFollow 컴포넌트를 찾을 수 없습니다. 카메라 줌 기능이 작동하지 않을 수 있습니다.");
        }

        /// <summary>
        /// Distance 필드 찾기 (리플렉션 사용)
        /// </summary>
        private void FindDistanceField(System.Type type)
        {
            // Cinemachine 3.0에서 사용 가능한 여러 필드명 시도
            string[] possibleFieldNames = { 
                "Distance", 
                "DefaultDistance", 
                "CameraDistance", 
                "m_Distance",
                "CameraRadius",
                "Radius"
            };
            
            // 필드 찾기
            foreach (string fieldName in possibleFieldNames)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(double)))
                {
                    distanceField = field;
                    Utils.CreateLogMessage<ThirdPersonCameraController>($"Found Distance field: {fieldName} (Type: {field.FieldType.Name})");
                    return;
                }
            }
            
            // 프로퍼티도 찾기
            string[] possiblePropertyNames = { 
                "Distance", 
                "DefaultDistance", 
                "CameraDistance"
            };
            
            foreach (string propertyName in possiblePropertyNames)
            {
                PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanWrite && 
                    (property.PropertyType == typeof(float) || property.PropertyType == typeof(double)))
                {
                    // 프로퍼티는 FieldInfo로 직접 저장할 수 없으므로, 별도로 처리 필요
                    // 여기서는 필드만 찾는 것으로 제한
                    Utils.CreateLogMessage<ThirdPersonCameraController>($"Found Distance property: {propertyName} (but using field access)");
                }
            }
            
            Utils.CreateLogMessage<ThirdPersonCameraController>($"Distance 필드를 찾을 수 없습니다. (Type: {type.Name})");
        }

        /// <summary>
        /// CinemachineCamera 초기 설정
        /// </summary>
        private void SetupCinemachineCamera()
        {
            if (cinemachineCamera == null) return;

            if (followTarget != null)
            {
                Utils.CreateLogMessage<ThirdPersonCameraController>($"Follow Target: {followTarget.name}");
            }

            if (cameraTarget != null)
            {
                Utils.CreateLogMessage<ThirdPersonCameraController>($"Camera Target: {cameraTarget.name}");
            }
        }

        /// <summary>
        /// 마우스 입력 처리
        /// </summary>
        private void HandleMouseInput()
        {
            // 마우스 우클릭 드래그 감지
            if (Input.GetMouseButtonDown(1))
            {
                isRightMouseButtonDown = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (Input.GetMouseButtonUp(1))
            {
                isRightMouseButtonDown = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // 마우스 휠 줌 입력
            float scrollInput = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                currentZoomDistance -= scrollInput * zoomSpeed;
                currentZoomDistance = Mathf.Clamp(currentZoomDistance, minZoomDistance, maxZoomDistance);
            }
        }

        /// <summary>
        /// 카메라 회전 업데이트
        /// </summary>
        private void UpdateCameraRotation()
        {
            if (!isRightMouseButtonDown || cameraTarget == null) return;

            Vector2 mouseDelta = new Vector2(
                Input.GetAxis("Mouse X"),
                Input.GetAxis("Mouse Y")
            );

            // 마우스 이동량이 너무 작으면 무시
            if (mouseDelta.magnitude < 0.01f) return;

            // 수평 회전 (Y축)
            currentHorizontalAngle += mouseDelta.x * mouseSensitivity;

            // 수직 회전 (X축) - 제한
            currentVerticalAngle -= mouseDelta.y * mouseSensitivity;
            currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, minVerticalAngle, maxVerticalAngle);

            // Camera Target의 회전 업데이트
            Quaternion targetRotation = Quaternion.Euler(currentVerticalAngle, currentHorizontalAngle, 0f);
            cameraTarget.rotation = Quaternion.Slerp(
                cameraTarget.rotation,
                targetRotation,
                Time.deltaTime * rotationSmoothness
            );
        }

        /// <summary>
        /// 카메라 줌 업데이트
        /// </summary>
        private void UpdateCameraZoom()
        {
            if (thirdPersonFollowComponent == null || distanceField == null) return;

            try
            {
                // 리플렉션을 사용하여 Distance 필드 업데이트
                distanceField.SetValue(thirdPersonFollowComponent, currentZoomDistance);
            }
            catch (System.Exception e)
            {
                // 리플렉션 실패 시 로그만 출력 (에러 방지)
                if (Time.frameCount % 60 == 0) // 1초마다 한 번만 로그
                {
                    Utils.CreateLogError<ThirdPersonCameraController>($"Zoom 업데이트 실패: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Follow Target 설정
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;
            SetupCinemachineCamera();
        }

        /// <summary>
        /// Camera Target 설정
        /// </summary>
        public void SetCameraTarget(Transform target)
        {
            cameraTarget = target;
            if (cameraTarget != null)
            {
                currentHorizontalAngle = cameraTarget.eulerAngles.y;
                currentVerticalAngle = cameraTarget.eulerAngles.x;
            }
        }

        /// <summary>
        /// 카메라 거리 설정
        /// </summary>
        public void SetCameraDistance(float distance)
        {
            currentZoomDistance = Mathf.Clamp(distance, minZoomDistance, maxZoomDistance);
        }
        #endregion
    }
}

