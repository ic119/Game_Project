using JJORY.Util;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace JJORY.Controller.UI
{
    public class UI_RegistPopupController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TMP_InputField account_InputField;
        [SerializeField] private TMP_InputField password_InputField;
        [SerializeField] private Button cancel_Button;
        [SerializeField] private Button craete_Button;

        [Header("계정 정보 변수")]
        private string account_Value;
        private string password_Value;
        private const string account_Key = "Player_Account";
        private const string password_Key = "Player_Password";

        [Header("키보드 액션")]
        [SerializeField] private TMP_InputField[] inputField_Arr;
        private int cur_Index = 0;
        #endregion

        #region LifeCycle
        private void Start()
        {
            Init();
            cancel_Button.onClick.AddListener(OnClickCancelButton);
            craete_Button.onClick.AddListener(OnClickCraeteButton);
        }

        private void OnEnable()
        {
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                MoveToNextInputField();
            }
            else if (Input.GetKeyDown(KeyCode.Return))
            {
                OnClickCraeteButton();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnClickCancelButton();
            }
        }

        private void OnDisable()
        {
            account_InputField.text = "";
            password_InputField.text = "";
        }
        #endregion

        #region Method
        private void Init()
        {
            account_InputField.inputType = TMP_InputField.InputType.Standard;
            account_InputField.contentType = TMP_InputField.ContentType.Standard;

            password_InputField.inputType = TMP_InputField.InputType.Password;
            password_InputField.contentType = TMP_InputField.ContentType.Password;  

            inputField_Arr = new TMP_InputField[] { account_InputField, password_InputField };
        }
        
        private void RegistInfoSave()
        {
            if (string.IsNullOrEmpty(account_Value))
            {
                Utils.CreateLogMessage<UI_RegistPopupController>("아이디를 입력해주세요!");
                return;
            }
            if (string.IsNullOrEmpty(password_Value))
            {
                Utils.CreateLogMessage<UI_RegistPopupController>("패스워드를 입력해주세요!");
                return;
            }
            if (string.IsNullOrEmpty(account_Value) == false && string.IsNullOrEmpty(password_Value) == false)
            {
                account_Value = account_InputField.text;
                password_Value = password_InputField.text;

                PlayerPrefs.SetString(account_Key, account_Value);
                PlayerPrefs.SetString(password_Key, password_Value);
                PlayerPrefs.Save();
                Utils.CreateLogMessage<UI_RegistPopupController>("회원가입 성공!");
                gameObject.SetActive(false);
            }
        }

        private void OnClickCancelButton()
        {
            account_InputField.text = "";
            password_InputField.text = "";
            gameObject.SetActive(false);
            Utils.CreateLogMessage<UI_RegistPopupController>("취소 버튼 클릭");
        }

        private void OnClickCraeteButton()
        {
            RegistInfoSave();

            gameObject.SetActive(false);
            Utils.CreateLogMessage<UI_RegistPopupController>("저장 버튼 클릭");
        }

        private void MoveToNextInputField()
        {
            if (inputField_Arr == null || inputField_Arr.Length == 0)
            {
                return;
            }

            for (int i = 0; i < inputField_Arr.Length; i++)
            {
                if (inputField_Arr[i].isFocused == true)
                {
                    cur_Index = i;
                    break;
                }
            }

            cur_Index = (cur_Index + 1) % inputField_Arr.Length;
            inputField_Arr[cur_Index].ActivateInputField();
        }
        #endregion
    }
}