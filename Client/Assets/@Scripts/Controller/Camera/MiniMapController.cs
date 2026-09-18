using Incheol.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.Controller
{
    /// <summary>
    /// GameScene의 미니맵을 담당한다. 플레이어 머리 위에서 수직으로 내려다보는 전용 카메라를 직접 생성해
    /// RenderTexture로 렌더링하고, 그 결과를 UI_GameSceneView.miniMapView(RawImage)에 표시한다.
    /// 카메라는 플레이어의 XZ 위치만 따라가고 회전은 항상 고정(정북이 위)이므로, 플레이어가 방향을
    /// 바꿔도 미니맵 자체는 회전하지 않는다(북쪽 고정 방식 - 플레이어 시점 회전 방식과 반대).
    /// 플레이어 위치 표시는 playerMiniMapIcon 스프라이트로 만든 UI Image를 미니맵 위에 올려두고,
    /// 미니맵 카메라 기준 뷰포트 좌표로 매 프레임 갱신한다. 카메라가 플레이어를 그대로 따라가므로
    /// 결과적으로 항상 중앙 부근에 위치하지만, 실제 월드 좌표를 투영하는 방식이라 이후 카메라를
    /// 맵 전체를 비추는 고정형으로 바꾸더라도 이 위치 계산 로직 자체는 그대로 재사용할 수 있다.
    /// </summary>
    public class MiniMapController : MonoBehaviour
    {
        #region Variable
        [Header("Camera")]
        [SerializeField, Min(1f)] private float cameraHeight = 30f;
        [SerializeField, Min(1f)] private float orthographicSize = 15f;
        [SerializeField] private LayerMask cullingMask = ~0;
        [SerializeField, Min(32)] private int renderTextureSize = 256;

        [Header("Player Icon")]
        [SerializeField] private Vector2 playerIconSize = new Vector2(24f, 24f);

        // 항상 정북(+Z)이 위로 오도록 고정된 회전값. 플레이어의 Y축 회전을 전혀 반영하지 않으므로
        // 플레이어가 회전해도 미니맵은 돌지 않는다.
        private static readonly Quaternion TopDownRotation = Quaternion.Euler(90f, 0f, 0f);

        private Camera miniMapCamera;
        private RenderTexture renderTexture;
        private RawImage miniMapView;
        private RectTransform playerIconRect;
        private Transform playerTransform;
        #endregion

        #region LifeCycle
        private void LateUpdate()
        {
            if (playerTransform == null)
            {
                return;
            }

            UpdateCameraTransform();
            UpdatePlayerIconPosition();
        }

        private void OnDestroy()
        {
            if (miniMapView != null && miniMapView.texture == renderTexture)
            {
                miniMapView.texture = null;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            if (miniMapCamera != null)
            {
                Destroy(miniMapCamera.gameObject);
            }

            if (playerIconRect != null)
            {
                Destroy(playerIconRect.gameObject);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// GameSceneManager가 UI_GameScene과 로컬 플레이어가 모두 준비된 시점에 한 번 호출한다.
        /// </summary>
        public void Initialize(RawImage _miniMapView, Sprite _playerIconSprite, Transform _playerTransform)
        {
            if (_miniMapView == null || _playerTransform == null)
            {
                DebugLogManager.GenerateErrorMessage<MiniMapController>("miniMapView 또는 playerTransform이 없어 미니맵을 초기화할 수 없습니다.");
                return;
            }

            miniMapView = _miniMapView;
            playerTransform = _playerTransform;

            CreateMiniMapCamera();
            CreatePlayerIcon(_playerIconSprite);
            UpdateCameraTransform();
            UpdatePlayerIconPosition();
        }

        private void CreateMiniMapCamera()
        {
            renderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16)
            {
                name = "MiniMapRenderTexture"
            };

            GameObject cameraObject = new GameObject("MiniMapCamera");
            cameraObject.transform.SetParent(transform, false);

            miniMapCamera = cameraObject.AddComponent<Camera>();
            miniMapCamera.orthographic = true;
            miniMapCamera.orthographicSize = orthographicSize;
            miniMapCamera.cullingMask = cullingMask;
            miniMapCamera.targetTexture = renderTexture;
            miniMapCamera.clearFlags = CameraClearFlags.SolidColor;
            miniMapCamera.backgroundColor = Color.black;

            miniMapView.texture = renderTexture;
        }

        private void CreatePlayerIcon(Sprite _playerIconSprite)
        {
            GameObject iconObject = new GameObject("PlayerMiniMapIcon", typeof(RectTransform));
            iconObject.transform.SetParent(miniMapView.rectTransform, false);

            Image iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = _playerIconSprite;
            iconImage.raycastTarget = false;

            playerIconRect = iconObject.GetComponent<RectTransform>();
            playerIconRect.anchorMin = playerIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            playerIconRect.pivot = new Vector2(0.5f, 0.5f);
            playerIconRect.sizeDelta = playerIconSize;
        }

        /// <summary>
        /// 카메라의 XZ 위치만 플레이어를 따라가고, 회전은 TopDownRotation으로 고정한다.
        /// </summary>
        private void UpdateCameraTransform()
        {
            Vector3 position = playerTransform.position;
            miniMapCamera.transform.SetPositionAndRotation(
                new Vector3(position.x, position.y + cameraHeight, position.z),
                TopDownRotation);
        }

        /// <summary>
        /// 미니맵 카메라 기준 뷰포트 좌표로 플레이어 월드 위치를 투영해 아이콘의 UI 위치를 갱신한다.
        /// </summary>
        private void UpdatePlayerIconPosition()
        {
            if (playerIconRect == null)
            {
                return;
            }

            Vector3 viewportPoint = miniMapCamera.WorldToViewportPoint(playerTransform.position);
            Rect rect = miniMapView.rectTransform.rect;

            playerIconRect.anchoredPosition = new Vector2(
                (viewportPoint.x - 0.5f) * rect.width,
                (viewportPoint.y - 0.5f) * rect.height);
        }
        #endregion
    }
}
