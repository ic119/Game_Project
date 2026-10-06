using System;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public class UI_MapViewPopupView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private GameObject container;
        [SerializeField] private Button closeButton;
        [SerializeField] private RawImage mapView;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// container가 현재 활성화되어 있으면 true.
        /// </summary>
        public bool IsOpen => container != null && container.activeSelf;

        /// <summary>
        /// 현재 맵 전체를 그리는 WorldMapController(Presenter가 생성/초기화)가 참조할 RawImage.
        /// </summary>
        public RawImage MapView => mapView;

        /// <summary>
        /// container가 활성화된 직후(rect가 유효한 시점) 발생한다.
        /// </summary>
        public event Action Opened;

        /// <summary>
        /// container가 비활성화된 직후 발생한다.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// container가 비활성화 상태일 때만 활성화한다(M키).
        /// </summary>
        public void Open()
        {
            if (container != null && !container.activeSelf)
            {
                container.SetActive(true);
                Opened?.Invoke();
            }
        }

        /// <summary>
        /// container가 활성화 상태일 때만 비활성화한다(closeButton).
        /// </summary>
        public void Close()
        {
            if (container != null && container.activeSelf)
            {
                container.SetActive(false);
                Closed?.Invoke();
            }
        }
        #endregion
    }
}
