using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// 내 캐릭터가 사망했을 때 부활까지 남은 시간을 "부활까지 N초"로 보여주는 팝업. GameSceneManager가 사망 시 Show,
    /// 부활 시 Hide를 호출한다. 실제 부활 시점은 서버가 정하므로(Game_PlayerRevived), 카운트다운이 0이 돼도 스스로 닫지 않고
    /// 부활 알림을 받아 Hide가 호출될 때 닫힌다.
    /// </summary>
    public class UI_PlayerRespawnPopupView : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private GameObject container;
        [SerializeField] private TextMeshProUGUI contentsText;

        // 카운트다운 중이면 true.
        private bool isCountingDown;

        // 부활까지 남은 시간(초).
        private float remainingSeconds;

        // 마지막으로 표시한 초. 초가 바뀔 때만 텍스트를 갱신한다.
        private int displayedSeconds = -1;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            // 프리팹에서 컨테이너가 켜진 채로 저장돼 있어도 게임 시작 시에는 보이지 않게 한다.
            Hide();
        }

        private void Update()
        {
            if (!isCountingDown)
            {
                return;
            }

            // 일시정지/시간 배율과 무관하게 실제 시간으로 줄인다(서버의 부활 타이머도 실제 시간이다).
            remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.unscaledDeltaTime);
            RefreshText();
        }
        #endregion

        #region Method
        /// <summary>
        /// 컨테이너를 켜고 _seconds초부터 카운트다운을 시작한다.
        /// </summary>
        public void Show(float _seconds)
        {
            remainingSeconds = Mathf.Max(0f, _seconds);
            displayedSeconds = -1;
            isCountingDown = true;

            if (container != null)
            {
                container.SetActive(true);
            }

            RefreshText();
        }

        /// <summary>
        /// 카운트다운을 멈추고 컨테이너를 끈 뒤 텍스트를 비운다.
        /// </summary>
        public void Hide()
        {
            isCountingDown = false;
            remainingSeconds = 0f;
            displayedSeconds = -1;

            if (container != null)
            {
                container.SetActive(false);
            }

            if (contentsText != null)
            {
                contentsText.text = string.Empty;
            }
        }

        // 남은 시간을 올림한 초로 표시한다(4.2초 남음 -> "부활까지 5초"). 0이 되면 서버의 부활 알림을 기다리는 동안 "곧 부활합니다"를 보여준다.
        private void RefreshText()
        {
            int seconds = Mathf.CeilToInt(remainingSeconds);
            if (seconds == displayedSeconds || contentsText == null)
            {
                return;
            }

            displayedSeconds = seconds;
            contentsText.text = seconds > 0 ? $"부활까지 {seconds}초" : "곧 부활합니다";
        }
        #endregion
    }
}
