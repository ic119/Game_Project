using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// 월드 스페이스 UI(캔버스 등)를 매 프레임 카메라 쪽으로 회전시키는 공용 빌보드 컴포넌트.
    /// 원래 UI_NameLabel에 있던 로직을 그대로 옮긴 것으로, 닉네임표뿐 아니라 상호작용 프롬프트처럼
    /// 특정 3D 오브젝트에 붙어 항상 카메라를 정면으로 바라봐야 하는 World Space UI라면 재사용한다.
    /// </summary>
    public class BillboardRotator : MonoBehaviour
    {
        public enum Mode
        {
            CameraRotation, // 카메라 평면과 평행하게 회전 (원근 왜곡 없이 항상 스크린 정면 유지)
            LookAtCamera    // 카메라 위치를 직접 바라보도록 회전
        }

        [Header("Billboard Settings")]
        [SerializeField] private Mode billboardMode = Mode.CameraRotation;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool useFixedYAxis = false;

        public Camera TargetCamera => targetCamera;

        private void LateUpdate()
        {
            UpdateBillboardRotation();
        }

        /// <summary>
        /// 빌보드가 바라볼 대상을 특정 카메라로 지정한다 (예: 프리뷰 카메라 등).
        /// 지정하지 않을 경우 Camera.main 또는 활성 카메라를 자동으로 탐색한다.
        /// </summary>
        public void SetTargetCamera(Camera camera)
        {
            targetCamera = camera;
        }

        /// <summary>
        /// 매 프레임(LateUpdate) 카메라를 향해 정면이 보이도록 회전한다.
        /// </summary>
        private void UpdateBillboardRotation()
        {
            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                targetCamera = Camera.main;
                if (targetCamera == null)
                {
                    targetCamera = FindAnyObjectByType<Camera>();
                }
            }

            if (targetCamera == null)
            {
                return;
            }

            if (billboardMode == Mode.CameraRotation)
            {
                if (useFixedYAxis)
                {
                    Vector3 cameraEuler = targetCamera.transform.rotation.eulerAngles;
                    transform.rotation = Quaternion.Euler(0f, cameraEuler.y, 0f);
                }
                else
                {
                    transform.rotation = targetCamera.transform.rotation;
                }
            }
            else
            {
                Vector3 directionToCamera = transform.position - targetCamera.transform.position;

                if (useFixedYAxis)
                {
                    directionToCamera.y = 0f;
                }

                if (directionToCamera.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(directionToCamera, targetCamera.transform.up);
                }
            }
        }
    }
}
