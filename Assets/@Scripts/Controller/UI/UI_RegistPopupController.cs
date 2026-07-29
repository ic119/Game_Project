using Incheol.Define;
using Incheol.Module;
using Incheol.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.Controller.UI
{
    public class UI_RegistPopupController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TMP_InputField account_InputField;
        [SerializeField] private TMP_InputField email_InputField;
        [SerializeField] private TMP_InputField password_InputField;
        [SerializeField] private Button cancel_Button;
        [SerializeField] private Button craete_Button;

        [Header("로그인 인증 관련")]
        [SerializeField] private PlayFabAuthService playFabAuthService;
        private IAuthService authService;
        private bool isRegisterInFlight;

        [Header("키보드 액션")]
        [SerializeField] private TMP_InputField[] inputField_Arr;
        private int cur_Index = 0;
        #endregion

        #region LifeCycle
        private void Start()
        {
            Init();
        }

        private void OnEnable()
        {
            cancel_Button.onClick.AddListener(OnClickCancelButton);
            craete_Button.onClick.AddListener(OnClickCraeteButton);
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
            cancel_Button.onClick.RemoveListener(OnClickCancelButton);
            craete_Button.onClick.RemoveListener(OnClickCraeteButton);
            account_InputField.text = "";
            email_InputField.text = "";
            password_InputField.text = "";
        }
        #endregion

        #region Method
        private void Init()
        {
            account_InputField.inputType = TMP_InputField.InputType.Standard;
            account_InputField.contentType = TMP_InputField.ContentType.Standard;

            email_InputField.inputType = TMP_InputField.InputType.Standard;
            email_InputField.contentType = TMP_InputField.ContentType.EmailAddress;

            password_InputField.inputType = TMP_InputField.InputType.Password;
            password_InputField.contentType = TMP_InputField.ContentType.Password;  

            inputField_Arr = new TMP_InputField[] { account_InputField, email_InputField, password_InputField };

            authService = playFabAuthService != null
                ? playFabAuthService
                : (FindFirstObjectByType<PlayFabAuthService>() ?? gameObject.AddComponent<PlayFabAuthService>());
        }

        private void TryRegister()
        {
            if (isRegisterInFlight)
            {
                return;
            }

            string account = account_InputField.text;
            string email = email_InputField.text;
            string password = password_InputField.text;

            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowAlarmPopup("계정 생성 오류", "아이디, 이메일, 비밀번호를 모두 입력해주세요.");
                return;
            }

            isRegisterInFlight = true;
            craete_Button.interactable = false;
            authService.RegisterAccount(account, password, email, OnRegisterComplete);
        }

        private void OnRegisterComplete(AuthResult _result)
        {
            isRegisterInFlight = false;
            craete_Button.interactable = true;

            if (_result.Success)
            {
                ShowAlarmPopup("계정 생성", "회원가입을 완료하였습니다.");
                gameObject.SetActive(false);
            }
            else
            {
                ShowAlarmPopup("계정 생성 실패", string.IsNullOrEmpty(_result.ErrorMessage)
                    ? "회원가입에 실패했습니다."
                    : _result.ErrorMessage);
            }
        }

        private void ShowAlarmPopup(string _title, string _message)
        {
            AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                transform.parent.gameObject.transform);
            EventController.Instance.InvokeShowPopup(_title, _message);
        }

        private void OnClickCancelButton()
        {
            account_InputField.text = "";
            email_InputField.text = "";
            password_InputField.text = "";
            gameObject.SetActive(false);
        }

        private void OnClickCraeteButton()
        {
            TryRegister();
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
