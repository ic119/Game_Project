using JJORY.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace JJORY.Controller.UI
{
    public class UI_LoginSceneController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TMP_InputField account_InputField;
        [SerializeField] private TMP_InputField password_InputField;
        [SerializeField] private Button regist_Button;
        [SerializeField] private Button login_Button;

        [Header("계정 생성 관련")]
        [SerializeField] private string account_Value;
        [SerializeField] private string password_Value;
        [SerializeField] private GameObject ui_RegistPopup;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            regist_Button.onClick.AddListener(OnClickRegistButton);
        }
        #endregion

        #region Method

        private void OnClickLoginButton()
        {

        }

        private void OnClickRegistButton()
        {
            if (ui_RegistPopup != null && ui_RegistPopup.activeSelf == false)
            {
                ui_RegistPopup.SetActive(true);
                Utils.CreateLogMessage<UI_LoginSceneController>("계정생성 버튼 클릭");
            }
        }
        #endregion
    }
}
