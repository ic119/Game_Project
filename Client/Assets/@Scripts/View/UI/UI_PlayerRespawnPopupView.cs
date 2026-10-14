using System;
using Incheol.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    /// <summary>
    /// 내 캐릭터가 사망했을 때 부활 방식(마을/제자리)을 고르는 팝업. 처음에는 "부활방식을 골라주세요"를 보여 주고,
    /// 마을 부활을 누르면 "N초 후 마을로 돌아갑니다", 제자리 부활을 누르면 "N초 후 제자리에서 부활합니다" 카운트다운을 보여 준다. GameSceneManager가 사망 시 Show,
    /// 부활 시 Hide를 호출한다. 실제 부활 시점과 위치는 서버가 정하므로(Game_PlayerRevived), 버튼을 눌러도/카운트다운이 0이 돼도
    /// 스스로 닫지 않고 부활 알림을 받아 Hide가 호출될 때 닫힌다.
    /// </summary>
    public class UI_PlayerRespawnPopupView : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private GameObject container;
        [SerializeField] private GameObject buttonRoots;
        [SerializeField] private TextMeshProUGUI contentsText;
        [SerializeField] private Button townReviveButton;
        [SerializeField] private Button inPlaceReviveButton;

        /// <summary>
        /// 부활 방식을 골랐을 때 발생한다. 인자는 제자리 부활 여부(true면 제자리, false면 마을).
        /// </summary>
        public event Action<bool> ReviveSelected;

        // 카운트다운 중이면 true.
        private bool isCountingDown;

        // 카운트다운 중인 부활 방식. true면 제자리, false면 마을.
        private bool countdownInPlace;

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

            if (townReviveButton != null)
            {
                townReviveButton.onClick.AddListener(HandleTownReviveClicked);
            }

            if (inPlaceReviveButton != null)
            {
                inPlaceReviveButton.onClick.AddListener(HandleInPlaceReviveClicked);
            }
        }

        private void OnDestroy()
        {
            if (townReviveButton != null)
            {
                townReviveButton.onClick.RemoveListener(HandleTownReviveClicked);
            }

            if (inPlaceReviveButton != null)
            {
                inPlaceReviveButton.onClick.RemoveListener(HandleInPlaceReviveClicked);
            }
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
        /// 컨테이너와 버튼을 켜고 "부활방식을 골라주세요"를 보여 준다. 카운트다운은 버튼을 눌렀을 때 시작한다.
        /// </summary>
        public void Show()
        {
            isCountingDown = false;
            remainingSeconds = 0f;
            displayedSeconds = -1;

            if (container != null)
            {
                container.SetActive(true);
            }

            if (buttonRoots != null)
            {
                buttonRoots.SetActive(true);
            }

            SetButtonsInteractable(true);

            if (contentsText != null)
            {
                contentsText.text = "부활방식을 골라주세요";
            }
        }

        // 부활 방식을 고른 뒤 _seconds초 카운트다운을 시작한다. 서버도 같은 시간 뒤 선택한 방식으로 부활시킨다.
        // 선택은 한 번만 할 수 있어 버튼 영역(buttonRoots)을 통째로 숨긴다. 다음 사망 때 Show가 다시 켠다.
        private void StartCountdown(bool _inPlace, float _seconds)
        {
            countdownInPlace = _inPlace;
            remainingSeconds = Mathf.Max(0f, _seconds);
            displayedSeconds = -1;
            isCountingDown = true;

            if (buttonRoots != null)
            {
                buttonRoots.SetActive(false);
            }

            SetButtonsInteractable(false);
            RefreshText();
        }

        private void SetButtonsInteractable(bool _interactable)
        {
            if (townReviveButton != null)
            {
                townReviveButton.interactable = _interactable;
            }

            if (inPlaceReviveButton != null)
            {
                inPlaceReviveButton.interactable = _interactable;
            }
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

            if (buttonRoots != null)
            {
                buttonRoots.SetActive(false);
            }

            if (contentsText != null)
            {
                contentsText.text = string.Empty;
            }
        }

        // 버튼을 눌러도 팝업은 닫지 않는다 - 서버가 부활을 승인해 Game_PlayerRevived가 오면 GameSceneManager가 Hide한다
        // (중복 클릭은 서버가 이미 부활한 플레이어의 요청이라 무시한다).
        private void HandleTownReviveClicked()
        {
            if (isCountingDown)
            {
                return;
            }

            ReviveSelected?.Invoke(false);
            StartCountdown(false, CombatTimings.TownReviveDelaySeconds);
        }

        private void HandleInPlaceReviveClicked()
        {
            if (isCountingDown)
            {
                return;
            }

            ReviveSelected?.Invoke(true);
            StartCountdown(true, CombatTimings.InPlaceReviveDelaySeconds);
        }

        // 남은 시간을 올림한 초로 표시한다(4.2초 남음 -> "5초 후 마을로 돌아갑니다" / "5초 후 제자리에서 부활합니다").
        // 0이 되면 서버의 부활 알림을 기다리는 동안 "곧 부활합니다"를 보여준다.
        private void RefreshText()
        {
            int seconds = Mathf.CeilToInt(remainingSeconds);
            if (seconds == displayedSeconds || contentsText == null)
            {
                return;
            }

            displayedSeconds = seconds;
            if (seconds <= 0)
            {
                contentsText.text = "곧 부활합니다";
                return;
            }

            contentsText.text = countdownInPlace ? $"{seconds}초 후 제자리에서 부활합니다" : $"{seconds}초 후 마을로 돌아갑니다";
        }
        #endregion
    }
}
