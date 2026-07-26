using Incheol.Module;
using Incheol.View.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.Controller.UI
{
    public class UI_AlarmPopupController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private Button confirm_Button;

        [Header("View 변수")]
        [SerializeField] private UI_AlarmPopupView alarmPopupView;
        #endregion

        #region LifeCycle
        private void Start()
        {
            if (alarmPopupView == null)
            {
                alarmPopupView = GetComponent<UI_AlarmPopupView>();
            }
        }

        private void OnEnable()
        {
            EventController.Instance.OnRequestShowPopup += GeneratePopup;
            confirm_Button.onClick.AddListener(OnClickConfirmButton);
        }

        private void OnDisable()
        {
            EventController.Instance.OnRequestShowPopup -= GeneratePopup;
            confirm_Button.onClick.RemoveListener(OnClickConfirmButton);
        }
        #endregion

        #region Method
        private void OnClickConfirmButton()
        {
            Destroy(gameObject);
        }

        /// <summary>
        /// 팝업창 생성
        /// </summary>
        private void GeneratePopup(string _title, string _content)
        {
            alarmPopupView.ContentGenerate(_title, _content);
        }
        #endregion
    }
}
