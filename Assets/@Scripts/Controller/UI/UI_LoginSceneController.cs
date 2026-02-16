using DG.Tweening;
using JJORY.Define;
using JJORY.Module;
using JJORY.Util;
using System.Collections;
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

        [Header("키보드 액션")]
        [SerializeField] private TMP_InputField[] inputField_Arr;
        private int cur_Index = 0;

        [Header("타이틀 관련 변수")]
        [SerializeField] private TextMeshProUGUI infoTitle;
        [SerializeField] private float duration = 0.3f; // 사라지는데 걸리는 시간
        private Tween blinkTween;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            //Init();

            //login_Button.onClick.AddListener(OnClickLoginButton);
            //regist_Button.onClick.AddListener(OnClickRegistButton);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                LoadMainSceneWithMask();
            }
        }

        private void OnDestroy()
        {
            //login_Button.onClick.RemoveListener(OnClickLoginButton);
            //regist_Button.onClick.RemoveListener(OnClickRegistButton);
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

        private void OnClickLoginButton()
        {
            string account_InputField_Value = account_InputField.text;
            string password_InputField_Value = password_InputField.text;

            string get_Account = PlayerPrefs.GetString(DEFINE.account_Key);
            string get_Password = PlayerPrefs.GetString(DEFINE.password_Key);

            Utils.CreateLogMessage<UI_LoginSceneController>("로그인 버튼 클릭");

            if (string.IsNullOrEmpty(account_InputField_Value))
            {
                Utils.CreateLogMessage<UI_LoginSceneController>("아이디를 입력해주세요!");
                return;
            }

            if (string.IsNullOrEmpty(password_InputField_Value))
            {
                Utils.CreateLogMessage<UI_LoginSceneController>("패스워드를 입력해주세요!");
                return;
            }

            if(account_InputField_Value.Equals(get_Account) && password_InputField_Value.Equals(get_Password))
            {
                Utils.CreateLogMessage<UI_LoginSceneController>("로그인 성공!");
                LoadMainSceneWithMask();
            }
            else if (account_InputField_Value.Equals(get_Account) == false)
            {
                Utils.CreateLogMessage<UI_LoginSceneController>("아이디를 올바르게 입력해주세요!");

                GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                                      gameObject.transform);
                Utils.CreateLogMessage<UI_RegistPopupController>($"transform : {gameObject.name}");
                UI_AlarmPopupController controller = popup.GetComponent<UI_AlarmPopupController>();
                EventController.Instance.InvokeShowPopup("계정 입력 오류", "올바른 계정을 입력해주세요!");
            }
            else if (password_InputField_Value.Equals(get_Password) == false)
            {
                Utils.CreateLogMessage<UI_LoginSceneController>("패스워드를 올바르게 입력해주세요!");

                GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.UI_AlarmPopup.ToString(),
                                                                                                      gameObject.transform);
                Utils.CreateLogMessage<UI_RegistPopupController>($"transform : {gameObject.name}");
                UI_AlarmPopupController controller = popup.GetComponent<UI_AlarmPopupController>();
                EventController.Instance.InvokeShowPopup("패스워드 입력 오류", "올바른 패스워드를 입력해주세요!");
            }
        }

        private void OnClickRegistButton()
        {
            if (ui_RegistPopup != null && ui_RegistPopup.activeSelf == false)
            {
                ui_RegistPopup.SetActive(true);
                Utils.CreateLogMessage<UI_LoginSceneController>("계정생성 버튼 클릭");
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
        /// DummySceneController의 MoveScene과 동일한 연출: CloseMask 후 LoadSceneByTags 호출
        /// </summary>
        private void LoadMainSceneWithMask()
        {
            UIController.Instance.CloseMask();
            SceneLoadController.Instance.LoadSceneByTags("Main");
        }
        #endregion
    }
}
