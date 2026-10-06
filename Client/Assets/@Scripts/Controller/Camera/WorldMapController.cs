using Incheol.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.Controller
{
    /// <summary>
    /// UI_MapViewPopupView.mapView(RawImage)에 현재 맵 전체를 보여준다. 미니맵(MiniMapController)이 플레이어를 따라가는
    /// 좁은 시야라면, 이 컨트롤러의 카메라는 맵 프리팹의 Renderer 영역(Bounds) 전체가 들어오도록 고정된 위치에서
    /// 수직으로 내려다본다(정북이 위). 팝업이 열려 있는 동안에만 카메라를 켜서 평소 렌더링 비용이 들지 않게 한다.
    /// 맵이 바뀔 수 있으므로(SwapMap) 팝업을 열 때마다 Show가 영역을 다시 계산한다.
    /// </summary>
    public class WorldMapController : MonoBehaviour
    {
        #region Variable
        [Header("Camera")]
        [SerializeField, Min(1f)] private float cameraHeightMargin = 30f;
        [SerializeField, Min(1f)] private float renderTextureMaxSize = 1024f;
        [SerializeField, Min(1f)] private float boundsPadding = 1.05f;
        [SerializeField] private LayerMask cullingMask = ~0;

        [Header("Player Icon")]
        [SerializeField] private Vector2 playerIconSize = new Vector2(24f, 24f);

        private static readonly Quaternion TopDownRotation = Quaternion.Euler(90f, 0f, 0f);

        private Camera worldMapCamera;
        private RenderTexture renderTexture;
        private RawImage mapView;
        private RectTransform playerIconRect;
        private Transform playerTransform;
        #endregion

        #region LifeCycle
        private void LateUpdate()
        {
            if (worldMapCamera != null && worldMapCamera.enabled)
            {
                UpdatePlayerIconPosition();
            }
        }

        private void OnDestroy()
        {
            ReleaseRenderTexture();

            if (worldMapCamera != null)
            {
                Destroy(worldMapCamera.gameObject);
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
        public void Initialize(RawImage _mapView, Sprite _playerIconSprite, Transform _playerTransform)
        {
            if (_mapView == null || _playerTransform == null)
            {
                DebugLogManager.GenerateErrorMessage<WorldMapController>("mapView 또는 playerTransform이 없어 월드맵을 초기화할 수 없습니다.");
                return;
            }

            mapView = _mapView;
            playerTransform = _playerTransform;

            GameObject cameraObject = new GameObject("WorldMapCamera");
            cameraObject.transform.SetParent(transform, false);

            worldMapCamera = cameraObject.AddComponent<Camera>();
            worldMapCamera.orthographic = true;
            worldMapCamera.cullingMask = cullingMask;
            worldMapCamera.clearFlags = CameraClearFlags.SolidColor;
            worldMapCamera.backgroundColor = Color.black;
            worldMapCamera.enabled = false;

            CreatePlayerIcon(_playerIconSprite);
        }

        /// <summary>
        /// _mapRoot(현재 맵 프리팹 인스턴스) 전체가 보이도록 카메라를 맞추고 렌더링을 시작한다.
        /// mapView의 rect(크기/비율)를 읽으므로 팝업 container가 활성화된 뒤에 호출해야 한다.
        /// </summary>
        public void Show(GameObject _mapRoot)
        {
            if (worldMapCamera == null || _mapRoot == null)
            {
                return;
            }

            if (!TryCalculateBounds(_mapRoot, out Bounds bounds))
            {
                DebugLogManager.GenerateErrorMessage<WorldMapController>($"{_mapRoot.name}에 Renderer가 없어 월드맵 영역을 계산할 수 없습니다.");
                return;
            }

            Rect rect = mapView.rectTransform.rect;
            float aspect = rect.height > 0f ? rect.width / rect.height : 1f;

            // 맵 전체가 잘리지 않도록 세로/가로 중 더 많이 필요한 쪽에 맞춘다(남는 쪽은 검은 여백).
            float halfHeight = bounds.extents.z;
            float halfWidth = bounds.extents.x;
            float orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect) * boundsPadding;

            float cameraHeight = bounds.max.y + cameraHeightMargin;
            worldMapCamera.orthographicSize = Mathf.Max(orthographicSize, 0.1f);
            worldMapCamera.nearClipPlane = 0.1f;
            worldMapCamera.farClipPlane = cameraHeight - bounds.min.y + cameraHeightMargin;
            worldMapCamera.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, cameraHeight, bounds.center.z),
                TopDownRotation);

            RebuildRenderTexture(aspect);

            worldMapCamera.enabled = true;
            UpdatePlayerIconPosition();
        }

        /// <summary>
        /// 팝업이 닫히면 카메라 렌더링을 멈춘다.
        /// </summary>
        public void Hide()
        {
            if (worldMapCamera != null)
            {
                worldMapCamera.enabled = false;
            }
        }

        private static bool TryCalculateBounds(GameObject _root, out Bounds _bounds)
        {
            Renderer[] renderers = _root.GetComponentsInChildren<Renderer>();
            _bounds = default;

            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds)
                {
                    _bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    _bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        /// <summary>
        /// mapView의 가로세로 비율과 같은 RenderTexture를 만들어 이미지가 늘어나 보이지 않게 한다.
        /// </summary>
        private void RebuildRenderTexture(float _aspect)
        {
            int width;
            int height;
            if (_aspect >= 1f)
            {
                width = Mathf.RoundToInt(renderTextureMaxSize);
                height = Mathf.Max(1, Mathf.RoundToInt(renderTextureMaxSize / _aspect));
            }
            else
            {
                height = Mathf.RoundToInt(renderTextureMaxSize);
                width = Mathf.Max(1, Mathf.RoundToInt(renderTextureMaxSize * _aspect));
            }

            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height)
            {
                return;
            }

            worldMapCamera.targetTexture = null;
            ReleaseRenderTexture();

            renderTexture = new RenderTexture(width, height, 16)
            {
                name = "WorldMapRenderTexture"
            };

            worldMapCamera.targetTexture = renderTexture;
            mapView.texture = renderTexture;
        }

        private void ReleaseRenderTexture()
        {
            if (renderTexture == null)
            {
                return;
            }

            if (mapView != null && mapView.texture == renderTexture)
            {
                mapView.texture = null;
            }

            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }

        private void CreatePlayerIcon(Sprite _playerIconSprite)
        {
            GameObject iconObject = new GameObject("PlayerWorldMapIcon", typeof(RectTransform));
            iconObject.transform.SetParent(mapView.rectTransform, false);

            Image iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = _playerIconSprite;
            iconImage.raycastTarget = false;

            playerIconRect = iconObject.GetComponent<RectTransform>();
            playerIconRect.anchorMin = playerIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            playerIconRect.pivot = new Vector2(0.5f, 0.5f);
            playerIconRect.sizeDelta = playerIconSize;
        }

        private void UpdatePlayerIconPosition()
        {
            if (playerIconRect == null || playerTransform == null)
            {
                return;
            }

            Vector3 viewportPoint = worldMapCamera.WorldToViewportPoint(playerTransform.position);
            Rect rect = mapView.rectTransform.rect;

            playerIconRect.anchoredPosition = new Vector2(
                (viewportPoint.x - 0.5f) * rect.width,
                (viewportPoint.y - 0.5f) * rect.height);
        }
        #endregion
    }
}
