using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public class UI_RegistPopupView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private Button createButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private UI_LoginSceneView loginSceneView;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            createButton.onClick.AddListener(OnClickCreateButton);
            cancelButton.onClick.AddListener(OnClickCancelButton);
        }
        #endregion

        #region Method
        private void OnClickCreateButton()
        {
            gameObject.SetActive(false);

            if (loginSceneView != null)
            {
                loginSceneView.SetRegistPopupActive(false);
            }
        }

        private void OnClickCancelButton()
        {
            gameObject.SetActive(false);

            if (loginSceneView != null)
            {
                loginSceneView.SetRegistPopupActive(false);
            }
        }
        #endregion
    }
}
