using Incheol.Controller;
using Incheol.View.UI;
using System;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    /// <summary>
    /// 게임 씬의 미니맵(MiniMapController)과 맵 팝업(M키, WorldMapController) 연결을 맡는다. UI_GameScene 인스턴스와
    /// 로컬 플레이어가 둘 다 준비된 시점에 한 번만 컨트롤러를 만들고, 팝업이 열릴 때마다 현재 맵 기준으로 영역을 다시 맞춘다.
    /// 컨트롤러 오브젝트는 parent(GameSceneManager) 밑에 만들어 씬과 함께 파괴된다.
    /// </summary>
    public class GameSceneMapPresenter
    {
        private readonly Transform parent;
        private readonly Func<GameObject> currentMapProvider;

        private UI_GameSceneView gameSceneView;
        private UI_MapViewPopupView mapViewPopupView;
        private MiniMapController miniMapController;
        private WorldMapController worldMapController;

        /// <param name="currentMapProvider">현재 로드된 맵 인스턴스. 맵 이동으로 바뀌므로 값이 아니라 조회 함수로 받는다.</param>
        public GameSceneMapPresenter(Transform parent, Func<GameObject> currentMapProvider)
        {
            this.parent = parent;
            this.currentMapProvider = currentMapProvider;
        }

        /// <summary>UI_GameScene 인스턴스가 만들어진 시점에 연결한다. 팝업이 없으면(null) 맵 팝업 기능만 꺼진다.</summary>
        public void AttachView(UI_GameSceneView gameSceneView, UI_MapViewPopupView mapViewPopupView)
        {
            this.gameSceneView = gameSceneView;
            this.mapViewPopupView = mapViewPopupView;
        }

        /// <summary>
        /// UI와 로컬 플레이어가 모두 준비된 시점에 호출한다. 이미 만들었으면 아무 일도 하지 않는다.
        /// 미니맵 뷰/맵 팝업 뷰가 인스펙터에 연결돼 있지 않으면 해당 기능만 조용히 건너뛴다.
        /// </summary>
        public void SetupForPlayer(Transform playerTransform)
        {
            if (playerTransform == null)
            {
                return;
            }

            SetupMiniMap(playerTransform);
            SetupWorldMap(playerTransform);
        }

        /// <summary>M키: 맵 팝업을 열고 닫는다.</summary>
        public void ToggleMapPopup()
        {
            if (mapViewPopupView == null)
            {
                return;
            }

            if (mapViewPopupView.IsOpen)
            {
                mapViewPopupView.Close();
            }
            else
            {
                mapViewPopupView.Open();
            }
        }

        /// <summary>씬이 파괴될 때 팝업 이벤트 구독을 해지한다.</summary>
        public void Dispose()
        {
            if (mapViewPopupView != null)
            {
                mapViewPopupView.Opened -= HandleMapViewOpened;
                mapViewPopupView.Closed -= HandleMapViewClosed;
            }
        }

        private void SetupMiniMap(Transform playerTransform)
        {
            if (miniMapController != null || gameSceneView == null || gameSceneView.MiniMapView == null)
            {
                return;
            }

            GameObject miniMapObject = new GameObject(nameof(MiniMapController));
            miniMapObject.transform.SetParent(parent, false);

            miniMapController = miniMapObject.AddComponent<MiniMapController>();
            miniMapController.Initialize(gameSceneView.MiniMapView, gameSceneView.PlayerMiniMapIcon, playerTransform);
        }

        private void SetupWorldMap(Transform playerTransform)
        {
            if (worldMapController != null || mapViewPopupView == null || mapViewPopupView.MapView == null)
            {
                return;
            }

            GameObject worldMapObject = new GameObject(nameof(WorldMapController));
            worldMapObject.transform.SetParent(parent, false);

            worldMapController = worldMapObject.AddComponent<WorldMapController>();
            worldMapController.Initialize(mapViewPopupView.MapView, gameSceneView != null ? gameSceneView.PlayerMiniMapIcon : null, playerTransform);

            mapViewPopupView.Opened += HandleMapViewOpened;
            mapViewPopupView.Closed += HandleMapViewClosed;
        }

        private void HandleMapViewOpened()
        {
            worldMapController?.Show(currentMapProvider());
        }

        private void HandleMapViewClosed()
        {
            worldMapController?.Hide();
        }
    }
}
