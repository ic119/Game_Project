using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace JJORY.Controller.UI
{
    public class UI_LoginSceneController : MonoBehaviour
    {
        #region Variable
        [Header("UI ����")]
        [SerializeField] private TMP_InputField account_InputField;
        [SerializeField] private TMP_InputField password_InputField;
        [SerializeField] private Button regist_Button;
        [SerializeField] private Button login_Button;

        [Header("���� ���� ����")]
        [SerializeField] private string account_Value;
        [SerializeField] private string password_Value;
        #endregion

        #region LifeCycle
        #endregion

        #region Method

        private void OnClickLoginButton()
        {

        }

        private void OnClickRegistButton()
        {

        }
        #endregion
    }
}
