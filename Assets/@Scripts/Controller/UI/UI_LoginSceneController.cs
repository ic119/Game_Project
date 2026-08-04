using Incheol.Module;
using Incheol.Util;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace Incheol.Controller.UI
{
    public class UI_LoginSceneController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TMP_InputField account_InputField;
        [SerializeField] private TMP_InputField password_InputField;
        [SerializeField] private Button regist_Button;
        [SerializeField] private Button login_Button;

        [SerializeField] private TextMeshProUGUI statusText;

        [Header("로그인 인증 관련")]
        [SerializeField] private PlayFabAuthService playFabAuthService;
        private IAuthService authService;
        private bool isLoginInFlight;

        [Header("계정 생성 관련")]
        [SerializeField] private string account_Value;
        [SerializeField] private string password_Value;
        [SerializeField] private GameObject ui_RegistPopup;

        [Header("키보드 액션")]
        [SerializeField] private TMP_InputField[] inputField_Arr;
        private int cur_Index = 0;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            Init();

            login_Button.onClick.AddListener(OnClickLoginButton);
            regist_Button.onClick.AddListener(OnClickRegistButton);
        }

        private void Start()
        {
            account_InputField.ActivateInputField();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                MoveToNextInputField();
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnClickLoginButton();
            }
        }

        private void OnDestroy()
        {
            login_Button.onClick.RemoveListener(OnClickLoginButton);
            regist_Button.onClick.RemoveListener(OnClickRegistButton);
        }
        #endregion

        #region Method
        /// <summary>
        /// InputField 초기화
        /// </summary>
        private void Init()
        {
            account_InputField.inputType = TMP_InputField.InputType.Standard;
            account_InputField.contentType = TMP_InputField.ContentType.Standard;

            password_InputField.inputType = TMP_InputField.InputType.Password;
            password_InputField.contentType = TMP_InputField.ContentType.Password;

            inputField_Arr = new TMP_InputField[] { account_InputField, password_InputField };

            authService = playFabAuthService != null ? playFabAuthService : gameObject.AddComponent<PlayFabAuthService>();
        }

        private void OnClickLoginButton()
        {
            if (isLoginInFlight)
            {
                return;
            }

            string account_InputField_Value = account_InputField.text;
            string password_InputField_Value = password_InputField.text;

            if (string.IsNullOrEmpty(account_InputField_Value))
            {
                ShowAlarmPopup("계정 입력 오류", "올바른 계정을 입력해주세요!");
                return;
            }

            if (string.IsNullOrEmpty(password_InputField_Value))
            {
                ShowAlarmPopup("계정 입력 오류", "올바른 패스워드를 입력해주세요!");
                return;
            }

            isLoginInFlight = true;
            SetLoginInteractable(false);
            authService.LoginWithAccount(account_InputField_Value, password_InputField_Value, OnLoginComplete);
        }

        private void OnClickRegistButton()
        {
            if (ui_RegistPopup != null && ui_RegistPopup.activeSelf == false)
            {
                ui_RegistPopup.SetActive(true);
            }
        }

        private void SetLoginInteractable(bool _interactable)
        {
            login_Button.interactable = _interactable;
            regist_Button.interactable = _interactable;
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

        /// <summary>
        /// CloseMask 트윈 재생 후 완료 시 Main 씬 로드
        /// </summary>
        private void LoadMainSceneWithMask()
        {
            LoadSceneController.Instance.LoadSceneByTags(AddressKey.UI_MainScene);
        }
        #endregion

        #region Playfab
        private void SetStatus(string _message)
        {
            Utils.CreateLogMessage<UI_LoginSceneController>(_message);
        }

        private void ShowAlarmPopup(string _title, string _message)
        {
            AddressableAssetController.Instance.Instantiate(AddressKey.UI_AlarmPopup, gameObject.transform);
            EventController.Instance.InvokeShowPopup(_title, _message);
        }

        private void OnLoginComplete(AuthResult _result)
        {
            isLoginInFlight = false;
            SetLoginInteractable(true);

            if (_result.Success)
            {
                LoadMainSceneWithMask();
            }
            else
            {
                SetStatus(_result.ErrorMessage);
                ShowAlarmPopup("로그인 실패", string.IsNullOrEmpty(_result.ErrorMessage)
                    ? "아이디 또는 비밀번호를 확인해주세요."
                    : _result.ErrorMessage);
            }
        }
        #endregion
    }
}
