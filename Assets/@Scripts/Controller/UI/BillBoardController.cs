using UnityEngine;
using TMPro;
using JJORY.Model.Player;

namespace JJORY.Controller.UI
{
    public class BillBoardController : MonoBehaviour
    {
        [Header("UI Reference")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private PlayerModel playerModel;

        private Transform targetCamera;
        private string cachedUserName = string.Empty;

        private void Awake()
        {
            if (playerModel == null)
            {
                playerModel = GetComponentInParent<PlayerModel>();
            }
        }

        private void Start()
        {
            UpdateNameLabel();
        }

        private void LateUpdate()
        {
            UpdateNameLabel();

            // 카메라 참조 확인
            if (targetCamera == null)
            {
                if (Camera.main != null)
                    targetCamera = Camera.main.transform;
                else
                    return;
            }

            // 실시간 빌보드 처리: 카메라의 회전값을 그대로 따름 (항상 카메라 정면 응시)
            Quaternion billboardRotation = targetCamera.rotation;

            if (canvas != null)
            {
                canvas.transform.rotation = billboardRotation;
            }
            else if (nameLabel != null)
            {
                nameLabel.transform.rotation = billboardRotation;
            }
        }

        private void UpdateNameLabel()
        {
            if (nameLabel == null || playerModel == null || playerModel.playerGameInfo == null)
            {
                return;
            }

            string currentUserName = playerModel.playerGameInfo.UserName;

            if (cachedUserName == currentUserName)
            {
                return;
            }

            cachedUserName = currentUserName;
            nameLabel.text = currentUserName;
        }
    }
}

