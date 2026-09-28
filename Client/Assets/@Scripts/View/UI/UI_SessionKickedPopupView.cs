using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    /// <summary>
    /// 서버가 이 접속을 강제로 끊었을 때(같은 캐릭터로 다른 곳에서 접속 등) 사유를 보여주고, 확인 버튼을 누르면
    /// 호출측(GameSceneManager)이 넘긴 동작(로그아웃 후 로그인 화면 이동)을 실행하는 팝업.
    /// 사용법: 팝업 UI 오브젝트에 이 컴포넌트를 붙이고 messageText/confirmButton을 연결한 뒤,
    /// UI_GameSceneView의 sessionKickedPopup 필드에 이 오브젝트를 연결하면 된다(처음에는 비활성 상태로 둬도 된다).
    /// 연결하지 않으면 GameSceneManager가 공용 알림 팝업(GameManager.ShowAlarmPopup)으로 대신 안내한다.
    /// </summary>
    public class UI_SessionKickedPopupView : MonoBehaviour
    {
        #region Variable
        [Tooltip("서버가 보낸 종료 사유를 표시할 텍스트")]
        [SerializeField] private TextMeshProUGUI messageText;

        [Tooltip("누르면 로그인 화면으로 돌아가는 확인 버튼")]
        [SerializeField] private Button confirmButton;

        private Action onConfirm;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(HandleConfirmClicked);
            }
        }

        private void OnDestroy()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 사유를 표시하고 팝업을 켠다. 확인 버튼을 누르면 _onConfirm을 한 번만 실행하고 팝업을 끈다.
        /// </summary>
        public void Show(string _message, Action _onConfirm)
        {
            if (messageText != null)
            {
                messageText.text = _message;
            }

            onConfirm = _onConfirm;
            gameObject.SetActive(true);
        }

        private void HandleConfirmClicked()
        {
            Action confirm = onConfirm;
            onConfirm = null;
            gameObject.SetActive(false);
            confirm?.Invoke();
        }
        #endregion
    }
}
