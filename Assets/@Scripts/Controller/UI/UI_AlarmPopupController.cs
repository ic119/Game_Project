using UnityEngine;
using UnityEngine.UI;

namespace JJORY.Controller.UI
{
    public class UI_AlarmPopupController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private Button confirm_Button;
        #endregion

        #region LifeCycle
        private void Start()
        {
            confirm_Button.onClick.AddListener(OnClickConfirmButton);
        }

        private void OnDestroy()
        {
            confirm_Button.onClick.RemoveListener(OnClickConfirmButton);
        }
        #endregion

        #region Method
        private void OnClickConfirmButton()
        {
            Destroy(gameObject);
        }
        #endregion
    }
}