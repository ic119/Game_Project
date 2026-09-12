using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// 캐릭터 머리 위에 위치하여 닉네임을 표시하고, 항상 카메라를 정면으로 바라보도록 회전하는 빌보드 UI 컴포넌트
    /// </summary>
    public class UI_NameLabel : MonoBehaviour
    {
        public enum BillboardMode
        {
            CameraRotation, // 카메라 평면과 평행하게 회전 (원근 왜곡 없이 항상 스크린 정면 유지)
            LookAtCamera    // 카메라 위치를 직접 바라보도록 회전
        }

        #region Variable
        [Header("UI Reference")]
        [SerializeField] private TextMeshProUGUI nicknameText;

        [Header("Billboard Settings")]
        [SerializeField] private BillboardMode billboardMode = BillboardMode.CameraRotation;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool useFixedYAxis = false;

        private string currentNickname = string.Empty;
        #endregion

        #region Property
        public string Nickname => currentNickname;
        public Camera TargetCamera => targetCamera;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (nicknameText == null)
            {
                nicknameText = GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        private void LateUpdate()
        {
            UpdateBillboardRotation();
        }
        #endregion

        #region Method
        /// <summary>
        /// 캐릭터의 닉네임을 설정하고 텍스트를 갱신한다.
        /// </summary>
        public void SetNickname(string nickname)
        {
            currentNickname = nickname ?? string.Empty;

            if (nicknameText != null)
            {
                nicknameText.text = currentNickname;
            }
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

            if (billboardMode == BillboardMode.CameraRotation)
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
        #endregion
    }
}
