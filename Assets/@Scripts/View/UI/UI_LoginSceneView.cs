using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public class UI_LoginSceneView : MonoBehaviour
    {
        #region Variable
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
        }

        private void Start()
        {
            FocusAccountInputField();
        }

        private void Update()
        {
            HandleTabNavigation();
        }
        #endregion

        #region Method
        private void FocusAccountInputField()
        {
            if (accountInputField == null)
            {
                Incheol.Utils.DebugLogManager.GenerateErrorMessage<UI_LoginSceneView>("accountInputField가 연결되어 있지 않습니다.");
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


        #endregion
    }
}
