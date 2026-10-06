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
        /// container가 비활성화 상태일 때만 활성화한다(M키).
        /// </summary>
        public void Open()
        {
            if (container != null && !container.activeSelf)
            {
                container.SetActive(true);
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
            }
        }
        #endregion
    }
}
