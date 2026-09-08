using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Incheol.Modules;

namespace Incheol.View.UI
{
    /// <summary>
    /// LobbyScene의 메인 UI. 캐릭터 유무에 따라 시작/생성/삭제 버튼 상태를 표시하고,
    /// 실제 세이브 데이터 처리(생성/삭제)는 이벤트로 상위 Presenter(LobbySceneManager)에 위임한다.
    /// </summary>
    public class UI_LobbySceneView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private GameObject maskImage;

        [Header("Lobby Scene Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button createButton;
        [SerializeField] private Button deleteButton;

        [Header("Selected Character")]
        [SerializeField] private TextMeshProUGUI selectedCharacterTitleText;

        [Header("Popups")]
        [SerializeField] private UI_CharacterCreatePopup characterCreatePopup;

        public UI_CharacterCreatePopup CharacterCreatePopup => characterCreatePopup;

        /// <summary>
        /// 시작(이어하기) 클릭 시 발생. 실제 씬 전환은 상위 Presenter가 담당한다.
        /// </summary>
        public event Action OnStartRequested;

        /// <summary>
        /// 삭제 클릭 시 발생. 실제 세이브 데이터 삭제는 상위 Presenter가 담당하고, 완료되면 RefreshState를 호출해줘야 한다.
        /// </summary>
        public event Action OnDeleteRequested;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (maskImage != null)
            {
                maskImage.SetActive(false);
            }

            if (characterCreatePopup == null)
            {
                characterCreatePopup = GetComponentInChildren<UI_CharacterCreatePopup>(true);
            }

            if (characterCreatePopup != null)
            {
                characterCreatePopup.OnCharacterCreated += OnCharacterCreated;
            }

            if (startButton != null) startButton.onClick.AddListener(OnClickStartButton);
            if (createButton != null) createButton.onClick.AddListener(OnClickCreateButton);
            if (deleteButton != null) deleteButton.onClick.AddListener(OnClickDeleteButton);

            RefreshState();
        }

        private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveAllListeners();
            if (createButton != null) createButton.onClick.RemoveAllListeners();
            if (deleteButton != null) deleteButton.onClick.RemoveAllListeners();

            if (characterCreatePopup != null)
            {
                characterCreatePopup.OnCharacterCreated -= OnCharacterCreated;
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// SaveDataManager의 로컬 캐시(HasSaveData/Load) 기준으로 버튼 상태와 선택된 캐릭터 표시를 갱신한다.
        /// 캐릭터 생성/삭제 직후 다시 호출해줘야 최신 상태가 반영된다.
        /// </summary>
        public void RefreshState()
        {
            bool hasSaveData = GameManager.Instance != null && GameManager.Instance.HasSaveData;

            if (startButton != null) startButton.gameObject.SetActive(hasSaveData);
            if (deleteButton != null) deleteButton.gameObject.SetActive(hasSaveData);
            if (createButton != null) createButton.gameObject.SetActive(!hasSaveData);

            if (selectedCharacterTitleText != null)
            {
                UserSaveData saveData = SaveDataManager.Instance != null ? SaveDataManager.Instance.Load() : null;
                selectedCharacterTitleText.text = saveData != null ? saveData.nickname : "캐릭터 없음";
            }
        }

        private void OnClickStartButton()
        {
            OnStartRequested?.Invoke();
        }

        private void OnClickCreateButton()
        {
            characterCreatePopup?.Open();
        }

        private void OnClickDeleteButton()
        {
            OnDeleteRequested?.Invoke();
        }

        private void OnCharacterCreated(UserSaveData _saveData)
        {
            RefreshState();
        }
        #endregion
    }
}
