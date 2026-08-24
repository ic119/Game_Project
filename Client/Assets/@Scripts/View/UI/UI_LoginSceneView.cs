using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Incheol.Modules;
using Incheol.Utils;
using System;

namespace Incheol.View.UI
{
    public class UI_LoginSceneView : MonoBehaviour
    {
        #region Variable

        [Header("로그인 변수")]
        [SerializeField] private Button loginButton;

        [Header("회원가입 변수")]
        [SerializeField] private Button registButton;
        [SerializeField] private GameObject registContainer;
        [SerializeField] private bool isRegistPopupActive = false;

        [Header("InputField 변수")]
        [SerializeField] private TMP_InputField accountInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        private TMP_InputField[] inputFieldOrder;

        [Header("Alarm 팝업창 변수")]
        [SerializeField] private GameObject alarmPopup;
        [SerializeField] private TextMeshProUGUI alarmTitleText;
        [SerializeField] private TextMeshProUGUI alarmContentText;
        [SerializeField] private Button checkButton;
        #endregion

        #region LifeCycle
private void Awake()
        {
            inputFieldOrder = new[] { accountInputField, passwordInputField };

            accountInputField.onSubmit.AddListener(_ => FocusNextInputField(accountInputField));
            passwordInputField.onSubmit.AddListener(_ => FocusNextInputField(passwordInputField));
            registButton.onClick.AddListener(OnClickRegistButton);
            loginButton.onClick.AddListener(OnClickLoginButton);
            checkButton.onClick.AddListener(CloseAlarmPopup);

            if (alarmPopup != null)
            {
                alarmPopup.SetActive(false);
            }
        }

        private void Update()
        {
            HandleTabNavigation();
        }
        #endregion

        #region Method

        public event Action LoginSucceeded;

        private void HandleTabNavigation()
        {
            if (isRegistPopupActive)
            {
                return;
            }

            if (Keyboard.current == null || !Keyboard.current.tabKey.wasPressedThisFrame)
            {
                return;
            }

            for (int i = 0; i < inputFieldOrder.Length; i++)
            {
                if (inputFieldOrder[i] != null && inputFieldOrder[i].isFocused)
                {
                    FocusNextInputField(inputFieldOrder[i]);
                    return;
                }
            }
        }

        private void FocusNextInputField(TMP_InputField _current)
        {
            int currentIndex = -1;
            for (int i = 0; i < inputFieldOrder.Length; i++)
            {
                if (inputFieldOrder[i] == _current)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                return;
            }

            TMP_InputField next = inputFieldOrder[(currentIndex + 1) % inputFieldOrder.Length];
            if (next == null)
            {
                return;
            }

            next.Select();

            next.ActivateInputField();
        }
        private void OnClickRegistButton()
        {
            if (registContainer != null)
            {
                registContainer.SetActive(true);
                isRegistPopupActive = true;
            }
        }
        public void SetRegistPopupActive(bool _isActive)
        {
            isRegistPopupActive = _isActive;
        }


        private void OnClickLoginButton()
        {
            if (isRegistPopupActive)
            {
                return;
            }

            if (string.IsNullOrEmpty(accountInputField.text) || string.IsNullOrEmpty(passwordInputField.text))
            {
                ShowAlarm("로그인 실패", "아이디와 비밀번호를 모두 입력해 주세요.");
                return;
            }

            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<UI_LoginSceneView>("ServerConnectManager.Instance가 null입니다.");
                return;
            }

            SetLoginInteractable(false);
            ServerConnectManager.Instance.Login(accountInputField.text, passwordInputField.text, OnLoginComplete);
        }

        private void OnLoginComplete(bool _success, string _error)
        {
            SetLoginInteractable(true);

            if (!_success)
            {
                ShowAlarm("로그인 실패", _error);
                return;
            }

            LoginSucceeded?.Invoke();
        }

        private void SetLoginInteractable(bool _interactable)
        {
            loginButton.interactable = _interactable;
            accountInputField.interactable = _interactable;
            passwordInputField.interactable = _interactable;
        }

        public void ShowAlarm(string _title, string _content)
        {
            if (alarmTitleText != null)
            {
                alarmTitleText.text = _title;
            }

            if (alarmContentText != null)
            {
                alarmContentText.text = _content;
            }

            if (alarmPopup != null)
            {
                alarmPopup.SetActive(true);
            }
        }

        private void CloseAlarmPopup()
        {
            if (alarmPopup != null)
            {
                alarmPopup.SetActive(false);
            }
        }

        #endregion
    }
}
