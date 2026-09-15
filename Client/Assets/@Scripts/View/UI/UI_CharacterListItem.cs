using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    /// <summary>
    /// 로비 캐릭터 목록(contentRect)의 개별 캐릭터 항목 UI. 닉네임/레벨/마지막 접속시간을 표시하고,
    /// 클릭 시 자신을 선택 대상으로 상위(UI_LobbySceneView)에 알린다.
    /// </summary>
    public class UI_CharacterListItem : MonoBehaviour
    {
        private const string EmptyLastLoginDisplay = "-";
        private const string LastLoginDisplayFormat = "yyyy-MM-dd HH:mm:ss";

        #region Variable
        [SerializeField] private Button selectButton;
        [SerializeField] private TextMeshProUGUI nicknameText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI lastLoginText;

        [Header("Selected State")]
        [SerializeField] private GameObject selectedHighlight;

        private CharacterSummary character;

        public CharacterSummary Character => character;

        /// <summary>
        /// 이 항목이 클릭되었을 때 자기 자신(CharacterSummary)을 담아 발생한다.
        /// </summary>
        public event Action<CharacterSummary> OnClicked;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (selectButton != null)
            {
                selectButton.onClick.AddListener(HandleClick);
            }
        }

        private void OnDestroy()
        {
            if (selectButton != null)
            {
                selectButton.onClick.RemoveListener(HandleClick);
            }
        }
        #endregion

        #region Method
        public void SetData(CharacterSummary _character)
        {
            character = _character;

            if (_character == null)
            {
                return;
            }

            if (nicknameText != null)
            {
                nicknameText.text = _character.nickname;
            }

            if (levelText != null)
            {
                levelText.text = _character.level.ToString();
            }

            if (lastLoginText != null)
            {
                lastLoginText.text = FormatLastLogin(_character.lastLoginAt);
            }
        }

        /// <summary>
        /// 서버가 내려준 ISO 8601(UTC) 문자열을 "yyyy-MM-dd HH:mm:ss" 형태의 로컬 시각으로 바꾼다.
        /// 값이 없거나 파싱에 실패하면(서버 미접속 이력 등) "-"로 표기한다.
        /// </summary>
        private static string FormatLastLogin(string _rawLastLoginAt)
        {
            if (string.IsNullOrEmpty(_rawLastLoginAt))
            {
                return EmptyLastLoginDisplay;
            }

            if (!DateTime.TryParse(_rawLastLoginAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return EmptyLastLoginDisplay;
            }

            return parsed.ToLocalTime().ToString(LastLoginDisplayFormat);
        }

        public void SetSelected(bool _isSelected)
        {
            if (selectedHighlight != null)
            {
                selectedHighlight.SetActive(_isSelected);
            }
        }

        private void HandleClick()
        {
            OnClicked?.Invoke(character);
        }
        #endregion
    }
}
