using JJORY.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JJORY.Controller.UI
{
    public class UI_RegistPopupController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TMP_InputField accout_InputField;
        [SerializeField] private TMP_InputField password_InputField;
        [SerializeField] private Button cancel_Button;
        [SerializeField] private Button craete_Button;

        [Header("계정 정보 변수")]
        private string account_Value;
        private string password_Value;
        #endregion

        #region LifeCycle
        private void Start()
        {
            accout_InputField.inputType = TMP_InputField.InputType.Standard;
            password_InputField.contentType = TMP_InputField.ContentType.Password;
            password_InputField.inputType = TMP_InputField.InputType.AutoCorrect;

            cancel_Button.onClick.AddListener(OnClickCancelButton);
            craete_Button.onClick.AddListener(OnClickCraeteButton);
        }

        private void OnDisable()
        {
            accout_InputField.text = "";
            password_InputField.text = "";
        }
        #endregion

        #region Method
        private void RegistInfoSave()
        {
            account_Value = accout_InputField.text;
            password_Value = password_InputField.text;
        }

        private void OnClickCancelButton()
        {
            Utils.CreateLogMessage<UI_RegistPopupController>("취소 버튼 클릭");
            gameObject.SetActive(false);
        }

        private void OnClickCraeteButton()
        {
            RegistInfoSave();
            Utils.CreateLogMessage<UI_RegistPopupController>("저장 버튼 클릭");

            gameObject.SetActive(false);
        }
        #endregion
    }
}