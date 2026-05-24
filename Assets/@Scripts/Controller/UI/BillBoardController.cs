using JJORY.Model.Player;
using TMPro;
using UnityEngine;


namespace JJORY.Controller.UI
{
    public class BillBoardController : MonoBehaviour
    {
        #region Variable
        [Header("UI Variable")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private TextMeshProUGUI nameLabel;

        [Header("BillBoard Options")]
        [SerializeField] private bool isUseDistanceScale = true;
        [SerializeField] private float baseScale = 0.01f;
        [SerializeField] private float scaleMultiplier = 0.002f;
        [SerializeField] private float minScale = 0.01f;
        [SerializeField] private float maxScale = 0.03f;

        [Header("Visibility")]
        [SerializeField] private bool isUseVisibleDistance = true;
        [SerializeField] private float visibleDistance = 25f;

        private Transform targetCamera;
        [SerializeField] private PlayerModel playerModel;
        #endregion

        #region LifeCycle
        private void OnEnable()
        {
            if (playerModel == null)
            {
                playerModel = GetComponent<PlayerModel>();
            }

            Camera mainCamera = Camera.main;
            if (targetCamera == null)
            {

                if (mainCamera == null)
                {
                    return;
                }

                targetCamera = mainCamera.transform;
            }

            Init(mainCamera, playerModel.playerGameInfo.UserName);
        }

        private void LateUpdate()
        {
            float distance = Vector3.Distance(transform.position, targetCamera.position);

            if (isUseVisibleDistance)
            {
                bool isVisible = distance <= visibleDistance;

                if (canvas != null && canvas.enabled != isVisible)
                {
                    canvas.enabled = isVisible;
                }

                if (!isVisible)
                {
                    return;
                }
            }

            LookAtCamera();

            if (isUseDistanceScale)
            {
                ApplyDistanceScale(distance);
            }
        }
        #endregion

        #region Method
        private void Init(Camera _camera, string _playerName)
        {
            if (_camera != null)
            {
                targetCamera = _camera.transform;
            }
            else
            {
                _camera = Camera.main;
                targetCamera = _camera.transform;
            }

            SetPlayerName(_playerName);
        }

        private void SetPlayerName(string _playerName)
        {
            if (nameLabel != null)
            {
                nameLabel.text = _playerName;
            }
        }

        private void LookAtCamera()
        {
            transform.rotation = Quaternion.LookRotation(transform.position - targetCamera.position);
        }

        private void ApplyDistanceScale(float distance)
        {
            float scale = Mathf.Clamp(baseScale + distance * scaleMultiplier,
                                      minScale,
                                      maxScale
            );

            transform.localScale = Vector3.one * scale;
        }
        #endregion
    }
}
