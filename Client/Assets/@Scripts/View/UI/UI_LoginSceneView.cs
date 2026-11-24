using DG.Tweening;
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

        private const string SavedLoginAccountKey = "SavedLoginAccount";

        [Header("로그인 변수")]
        [SerializeField] private Button loginButton;

        [Header("로그인 정보 저장")]
        [SerializeField] private Toggle loginDataSaveToggle;

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

        [Header("로그인 연출 (비워두면 자식에서 이름으로 탐색)")]
        [SerializeField] private RectTransform loginContainer;
        [SerializeField] private RectTransform backgroundRect;

        private const float ClickPunchDuration = 0.15f;
        private const float WaitingInputAlpha = 0.5f;
        private const float WaitingTextCycleSeconds = 1.2f;
        private const float SuccessFadeDuration = 0.4f;
        private const float SuccessRiseDistance = 40f;
        private const float SuccessBackgroundZoom = 1.08f;
        private const float SuccessBackgroundZoomDuration = 0.6f;
        private const float FailShakeDuration = 0.35f;
        private const string LoginWaitingText = "로그인 중";

        private CanvasGroup loginContainerGroup;
        private TMP_Text loginButtonText;
        private string loginButtonOriginalText;
        private float accountFieldOriginalAlpha = 1f;
        private float passwordFieldOriginalAlpha = 1f;
        private Vector2 loginContainerOriginalPos;
        private Vector3 backgroundOriginalScale = Vector3.one;

        private Tween clickPunchTween;
        private Tween waitingTextTween;
        private Tween accountFadeTween;
        private Tween passwordFadeTween;
        private Sequence resultSequence;
        #endregion

        #region LifeCycle
private void Awake()
        {
            inputFieldOrder = new[] { accountInputField, passwordInputField };

            CacheLoginEffectTargets();

            accountInputField.onValidateInput += InputFieldValidator.AllowEnglishAndDigitsOnly;
            accountInputField.onSubmit.AddListener(_ => FocusNextInputField(accountInputField));
            passwordInputField.onSubmit.AddListener(_ => FocusNextInputField(passwordInputField));
            registButton.onClick.AddListener(OnClickRegistButton);
            loginButton.onClick.AddListener(OnClickLoginButton);
            checkButton.onClick.AddListener(CloseAlarmPopup);

            if (alarmPopup != null)
            {
                alarmPopup.SetActive(false);
            }

            LoadSavedLoginAccount();

            if (loginDataSaveToggle != null)
            {
                loginDataSaveToggle.onValueChanged.AddListener(OnLoginDataSaveToggleChanged);
            }
        }

        private void Update()
        {
            HandleTabNavigation();
        }

        private void OnDestroy()
        {
            // SetLink로도 정리되지만, 대기 텍스트처럼 무한 루프인 트윈은 명시적으로 끊어 둔다.
            clickPunchTween?.Kill();
            waitingTextTween?.Kill();
            accountFadeTween?.Kill();
            passwordFadeTween?.Kill();
            resultSequence?.Kill();
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

            PlayClickPunch();
            SetLoginInteractable(false);
            PlayWaitingEffect();
            ServerConnectManager.Instance.Login(accountInputField.text, passwordInputField.text, OnLoginComplete);
        }

        private void OnLoginComplete(bool _success, string _error)
        {
            // 서버 응답이 오기 전에 화면이 파괴된 경우(씬 전환 등) 연출/알람을 건드리지 않는다.
            if (this == null)
            {
                return;
            }

            StopWaitingEffect();

            if (!_success)
            {
                PlayFailEffect(_error);
                return;
            }

            SaveAccountIfEnabled();

            // 입력은 잠근 채로 성공 연출을 끝낸 뒤에 LoginSucceeded를 알려, 씬 전환이 연출을 자르지 않게 한다.
            PlaySuccessEffect(() => LoginSucceeded?.Invoke());
        }

        #region Login Effect
        private void CacheLoginEffectTargets()
        {
            if (loginContainer == null)
            {
                loginContainer = transform.Find("LoginContainer") as RectTransform;
            }

            if (backgroundRect == null)
            {
                backgroundRect = transform.Find("BackGround") as RectTransform;
            }

            if (loginContainer != null)
            {
                loginContainerOriginalPos = loginContainer.anchoredPosition;

                if (!loginContainer.TryGetComponent(out loginContainerGroup))
                {
                    loginContainerGroup = loginContainer.gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (backgroundRect != null)
            {
                backgroundOriginalScale = backgroundRect.localScale;
            }

            loginButtonText = loginButton.GetComponentInChildren<TMP_Text>();
            if (loginButtonText != null)
            {
                loginButtonOriginalText = loginButtonText.text;
            }

            if (accountInputField.image != null)
            {
                accountFieldOriginalAlpha = accountInputField.image.color.a;
            }

            if (passwordInputField.image != null)
            {
                passwordFieldOriginalAlpha = passwordInputField.image.color.a;
            }
        }

        private void PlayClickPunch()
        {
            // 연타로 스케일이 누적되지 않도록 이전 펀치를 끝 상태로 마무리하고 시작한다.
            clickPunchTween?.Kill(true);
            clickPunchTween = loginButton.transform
                .DOPunchScale(Vector3.one * -0.1f, ClickPunchDuration, 8, 0.5f)
                .SetLink(gameObject);
        }

        private void PlayWaitingEffect()
        {
            if (loginButtonText != null)
            {
                waitingTextTween?.Kill();
                waitingTextTween = DOVirtual
                    .Float(0f, 4f, WaitingTextCycleSeconds, v =>
                    {
                        loginButtonText.text = LoginWaitingText + new string('.', Mathf.Min((int)v, 3));
                    })
                    .SetEase(Ease.Linear)
                    .SetLoops(-1, LoopType.Restart)
                    .SetLink(gameObject);
            }

            FadeInputField(accountInputField, WaitingInputAlpha, ref accountFadeTween);
            FadeInputField(passwordInputField, WaitingInputAlpha, ref passwordFadeTween);
        }

        /// <summary>대기 연출(점 애니메이션, 입력창 흐림)을 원래 상태로 되돌린다. 성공/실패 모두 가장 먼저 호출한다.</summary>
        private void StopWaitingEffect()
        {
            waitingTextTween?.Kill();
            waitingTextTween = null;

            if (loginButtonText != null)
            {
                loginButtonText.text = loginButtonOriginalText;
            }

            FadeInputField(accountInputField, accountFieldOriginalAlpha, ref accountFadeTween);
            FadeInputField(passwordInputField, passwordFieldOriginalAlpha, ref passwordFadeTween);
        }

        private void FadeInputField(TMP_InputField _field, float _alpha, ref Tween _tween)
        {
            _tween?.Kill();

            if (_field.image == null)
            {
                return;
            }

            Image image = _field.image;
            _tween = DOTween
                .To(() => image.color.a, a =>
                {
                    Color c = image.color;
                    c.a = a;
                    image.color = c;
                }, _alpha, 0.15f)
                .SetLink(gameObject);
        }

        private void PlaySuccessEffect(Action _onComplete)
        {
            if (loginContainer == null || loginContainerGroup == null)
            {
                _onComplete?.Invoke();
                return;
            }

            resultSequence?.Kill();
            loginContainerGroup.interactable = false;
            loginContainerGroup.blocksRaycasts = false;

            resultSequence = DOTween.Sequence().SetLink(gameObject);
            CanvasGroup group = loginContainerGroup;
            RectTransform container = loginContainer;
            float targetY = loginContainerOriginalPos.y + SuccessRiseDistance;

            resultSequence.Append(DOTween.To(() => group.alpha, a => group.alpha = a, 0f, SuccessFadeDuration).SetEase(Ease.InQuad));
            resultSequence.Join(DOTween
                .To(() => container.anchoredPosition.y, y => container.anchoredPosition = new Vector2(container.anchoredPosition.x, y), targetY, SuccessFadeDuration)
                .SetEase(Ease.OutQuad));

            if (backgroundRect != null)
            {
                resultSequence.Join(backgroundRect
                    .DOScale(backgroundOriginalScale * SuccessBackgroundZoom, SuccessBackgroundZoomDuration)
                    .SetEase(Ease.OutSine));
            }

            resultSequence.OnComplete(() => _onComplete?.Invoke());
        }

        private void PlayFailEffect(string _error)
        {
            void Finish()
            {
                SetLoginInteractable(true);
                ShowAlarm("로그인 실패", _error);
            }

            if (loginContainer == null)
            {
                Finish();
                return;
            }

            resultSequence?.Kill();
            loginContainer.anchoredPosition = loginContainerOriginalPos;

            // 흔들림이 끝난 뒤 입력을 풀고 알람을 띄워 연타/겹침을 막는다. 끝나면 원래 위치로 복귀한다.
            resultSequence = DOTween.Sequence().SetLink(gameObject);
            RectTransform container = loginContainer;
            Vector2 origin = loginContainerOriginalPos;
            resultSequence.Append(DOVirtual
                .Float(0f, 1f, FailShakeDuration, t =>
                {
                    // 시간이 지날수록 진폭이 줄어드는 좌우 흔들림.
                    float damper = 1f - t;
                    float offset = Mathf.Sin(t * 6f * Mathf.PI * 2f) * 18f * damper;
                    container.anchoredPosition = new Vector2(origin.x + offset, origin.y);
                })
                .SetEase(Ease.Linear));
            resultSequence.OnComplete(() =>
            {
                loginContainer.anchoredPosition = loginContainerOriginalPos;
                Finish();
            });
        }
        #endregion

        /// <summary>
        /// loginDataSaveToggle이 켜져 있으면 로그인 성공 시점의 아이디(계정)만 PlayerPrefs에 저장한다.
        /// 비밀번호는 평문으로 로컬에 남기면 안 되므로 저장 대상에서 제외한다.
        /// </summary>
        private void SaveAccountIfEnabled()
        {
            if (loginDataSaveToggle == null || !loginDataSaveToggle.isOn)
            {
                return;
            }

            PlayerPrefs.SetString(SavedLoginAccountKey, accountInputField.text ?? string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 저장된 아이디가 있으면 입력창에 채우고 토글도 켠 상태로 복원한다. 저장된 아이디가 없으면 토글은 꺼진 상태 그대로 둔다.
        /// </summary>
        private void LoadSavedLoginAccount()
        {
            string savedAccount = PlayerPrefs.GetString(SavedLoginAccountKey, string.Empty);
            bool hasSavedAccount = !string.IsNullOrEmpty(savedAccount);

            if (hasSavedAccount)
            {
                accountInputField.text = savedAccount;
            }

            if (loginDataSaveToggle != null)
            {
                loginDataSaveToggle.SetIsOnWithoutNotify(hasSavedAccount);
            }
        }

        /// <summary>
        /// 토글을 끄면 그 자리에서 즉시 저장된 아이디를 지운다(로그인을 다시 시도하지 않아도 "잊기"가 바로 반영되도록).
        /// 토글을 켜면 지금 입력창에 있는 아이디를 바로 저장한다.
        /// </summary>
        private void OnLoginDataSaveToggleChanged(bool _isOn)
        {
            if (!_isOn)
            {
                PlayerPrefs.DeleteKey(SavedLoginAccountKey);
                PlayerPrefs.Save();
                return;
            }

            SaveAccountIfEnabled();
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
