using Incheol.Define;
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
        [SerializeField] private Toggle loginInfoSaveToggle;

        [SerializeField] private TextMeshProUGUI statusText;

        [Header("로그인 인증 관련")]
        private IAuthService authService;

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
            loginInfoSaveToggle.onValueChanged.AddListener(OnLoginInfoSaveToggleChanged);
        }

        private void Start()
        {
            LoadSavedLoginInfo();
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
            loginInfoSaveToggle.onValueChanged.RemoveListener(OnLoginInfoSaveToggleChanged);
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
        }

        /// <summary>
        /// PlayerPrefs에 저장된 로그인 정보 저장 토글 상태를 불러오고,
        /// 체크되어 있는 경우에만 저장된 계정명을 account_InputField에 채운다.
        /// </summary>
        private void LoadSavedLoginInfo()
        {
            bool isSaveToggleOn = PlayerPrefs.GetInt(DEFINE.saveLoginInfoToggle_Key, 0) == 1;

            // 초기화 시점에는 리스너를 타지 않도록 SetIsOnWithoutNotify 사용
            loginInfoSaveToggle.SetIsOnWithoutNotify(isSaveToggleOn);

            account_InputField.text = isSaveToggleOn
                ? PlayerPrefs.GetString(DEFINE.savedLoginAccount_Key, string.Empty)
                : string.Empty;
        }

        /// <summary>
        /// 로그인 정보 저장 토글의 체크 여부를 PlayerPrefs에 즉시 반영한다.
        /// 체크 해제 시에는 저장되어 있던 계정명도 함께 제거한다.
        /// </summary>
        private void OnLoginInfoSaveToggleChanged(bool _isOn)
        {
            PlayerPrefs.SetInt(DEFINE.saveLoginInfoToggle_Key, _isOn ? 1 : 0);

            if (!_isOn)
            {
                PlayerPrefs.DeleteKey(DEFINE.savedLoginAccount_Key);
            }

            PlayerPrefs.Save();
        }

        /// <summary>
        /// 로그인 성공 시, 저장 토글이 체크된 경우에만 계정명을 저장한다.
        /// </summary>
        private void SaveLoginAccountIfNeeded(string _account)
        {
            if (loginInfoSaveToggle.isOn)
            {
                PlayerPrefs.SetString(DEFINE.savedLoginAccount_Key, _account);
            }
            else
            {
                PlayerPrefs.DeleteKey(DEFINE.savedLoginAccount_Key);
            }

            PlayerPrefs.Save();
        }

        private void OnClickLoginButton()
        {
            string account_InputField_Value = account_InputField.text;
            string password_InputField_Value = password_InputField.text;

            string get_Account = PlayerPrefs.GetString(DEFINE.account_Key);
            string get_Password = PlayerPrefs.GetString(DEFINE.password_Key);

            if (string.IsNullOrEmpty(account_InputField_Value))
            {
                GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                                      gameObject.transform);
                UI_AlarmPopupController controller = popup.GetComponent<UI_AlarmPopupController>();
                EventController.Instance.InvokeShowPopup("계정 입력 오류", "올바른 계정을 입력해주세요!");
                return;
            }

            if (string.IsNullOrEmpty(password_InputField_Value))
            {
                GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                                      gameObject.transform);
                UI_AlarmPopupController controller = popup.GetComponent<UI_AlarmPopupController>();
                EventController.Instance.InvokeShowPopup("계정 입력 오류", "올바른 계정을 입력해주세요!");
                return;
            }

            if(account_InputField_Value.Equals(get_Account) && password_InputField_Value.Equals(get_Password))
            {
                SaveLoginAccountIfNeeded(account_InputField_Value);
                GameManager.SetLoggedInUserName(account_InputField_Value);
                LoadMainSceneWithMask();
            }
            else if (account_InputField_Value.Equals(get_Account) == false)
            {
                GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                                      gameObject.transform);
                UI_AlarmPopupController controller = popup.GetComponent<UI_AlarmPopupController>();
                EventController.Instance.InvokeShowPopup("계정 입력 오류", "올바른 계정을 입력해주세요!");
            }
            else if (password_InputField_Value.Equals(get_Password) == false)
            {
                GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                                      gameObject.transform);
                UI_AlarmPopupController controller = popup.GetComponent<UI_AlarmPopupController>();
                EventController.Instance.InvokeShowPopup("패스워드 입력 오류", "올바른 패스워드를 입력해주세요!");
            }
        }

        private void OnClickRegistButton()
        {
            if (ui_RegistPopup != null && ui_RegistPopup.activeSelf == false)
            {
                ui_RegistPopup.SetActive(true);
            }
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
            SceneLoadController.Instance.LoadSceneByTags("Main");
        }
        #endregion

        #region Playfab
        private void SetStatus(string _message)
        {
            Utils.CreateLogMessage<UI_LoginSceneController>(_message);
        }

        private void OnLoginComplete(AuthResult _result)
        {
            if (_result.Success)
            {
            }
        }
        #endregion
    }
}
