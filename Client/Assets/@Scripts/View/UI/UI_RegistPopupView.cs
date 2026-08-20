using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Incheol.Modules;
using Incheol.Utils;

namespace Incheol.View.UI
{
    public class UI_RegistPopupView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private Button createButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private UI_LoginSceneView loginSceneView;
        [SerializeField] private TMP_InputField accountInputField;
        [SerializeField] private TMP_InputField nickNameInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private TMP_InputField passwordCheckInputField;
        private TMP_InputField[] inputFieldOrder;

        #endregion

        #region LifeCycle
private void Awake()
        {
            inputFieldOrder = new[] { accountInputField, passwordInputField, passwordCheckInputField };

            createButton.onClick.AddListener(OnClickCreateButton);
            cancelButton.onClick.AddListener(OnClickCancelButton);
            passwordInputField.onValueChanged.AddListener(OnPasswordValueChanged);
            passwordCheckInputField.onValueChanged.AddListener(OnPasswordValueChanged);

            accountInputField.onSubmit.AddListener(_ => FocusNextInputField(accountInputField));
            passwordInputField.onSubmit.AddListener(_ => FocusNextInputField(passwordInputField));
            passwordCheckInputField.onSubmit.AddListener(_ => FocusNextInputField(passwordCheckInputField));

            createButton.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            FocusAccountInputField();
        }

        private void Update()
        {
            HandleTabNavigation();
        }

        #endregion

        #region Method
private void Init()
        {
            accountInputField.text = null;
            nickNameInputField.text = null;
            passwordInputField.text = null;
            passwordCheckInputField.text = null;
            createButton.gameObject.SetActive(false);
        }

        private void FocusAccountInputField()
        {
            if (accountInputField == null)
            {
                Incheol.Utils.DebugLogManager.GenerateErrorMessage<UI_RegistPopupView>("accountInputField가 연결되어 있지 않습니다.");
                return;
            }

            if (Application.isMobilePlatform)
            {
                return;
            }

            accountInputField.Select();
            accountInputField.ActivateInputField();
        }

        private void HandleTabNavigation()
        {
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


private void OnClickCreateButton()
        {
            if (string.IsNullOrEmpty(accountInputField.text) || string.IsNullOrEmpty(nickNameInputField.text))
            {
                loginSceneView?.ShowAlarm("회원가입 실패", "아이디와 닉네임을 모두 입력해 주세요.");
                return;
            }

            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<UI_RegistPopupView>("ServerConnectManager.Instance가 null입니다.");
                return;
            }

            SetInteractable(false);
            ServerConnectManager.Instance.Register(accountInputField.text, passwordInputField.text, nickNameInputField.text, OnRegisterComplete);
        }

private void OnClickCancelButton()
        {
            ClosePopup();
        }


        private void OnRegisterComplete(bool _success, string _error)
        {
            SetInteractable(true);

            if (!_success)
            {
                loginSceneView?.ShowAlarm("회원가입 실패", _error);
                return;
            }

            ClosePopup();
        }

        private void SetInteractable(bool _interactable)
        {
            createButton.interactable = _interactable;
            cancelButton.interactable = _interactable;
            accountInputField.interactable = _interactable;
            nickNameInputField.interactable = _interactable;
            passwordInputField.interactable = _interactable;
            passwordCheckInputField.interactable = _interactable;
        }

        private void ClosePopup()
        {
            Init();
            gameObject.SetActive(false);

            if (loginSceneView != null)
            {
                loginSceneView.SetRegistPopupActive(false);
            }
        }


        private void OnPasswordValueChanged(string _)
        {
            bool isValid = !string.IsNullOrEmpty(passwordInputField.text)
                && !string.IsNullOrEmpty(passwordCheckInputField.text)
                && passwordInputField.text == passwordCheckInputField.text;

            createButton.gameObject.SetActive(isValid);
        }

        #endregion
    }
}
