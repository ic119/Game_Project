using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Incheol.Modules;
using Incheol.Utils;

namespace Incheol.View.UI
{
    /// <summary>
    /// LobbyScene의 메인 UI. 계정이 보유한 캐릭터 목록을 표시하고, 목록에서 캐릭터를 선택하면
    /// 시작/삭제 버튼이 그 캐릭터를 대상으로 동작하며 닉네임/레벨과 3D 외형이 함께 표시된다.
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

        [Header("CharacterList Container")]
        [SerializeField] private RectTransform contentRect;
        [SerializeField] private UI_CharacterListItem characterListItemPrefab;

        [Header("Selected Character")]
        [SerializeField] private TextMeshProUGUI selectedCharacterTitleText;
        [SerializeField] private RawImage selectedPreviewImage;
        [SerializeField] private CharacterPreviewStage previewStage;

        /// <summary>
        /// previewStage를 동적으로 생성할 때 배치할 월드 좌표. UI_CharacterCreatePopup의 프리뷰 스테이지와
        /// 같은 "CharacterPreview" 레이어/컬링마스크를 공유하므로, 두 스테이지가 동시에 존재해도
        /// 서로의 카메라에 겹쳐 찍히지 않도록 popup 쪽 기본 스폰 위치(500,500,500)와 충분히(원거리 클리핑 20 이상) 떨어뜨린다.
        /// </summary>
        [SerializeField] private Vector3 dynamicStageSpawnPosition = new Vector3(500f, 500f, 1500f);

        [Header("Popups")]
        [SerializeField] private UI_CharacterCreatePopup characterCreatePopup;

        private readonly List<UI_CharacterListItem> spawnedListItems = new List<UI_CharacterListItem>();
        private bool isPreviewStageDynamicallyCreated;
        private long? previewedCharacterId;
        private long? pendingPreviewCharacterId;

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
                characterCreatePopup.OnPopupClosed += OnCreatePopupClosed;
            }

            if (SaveDataManager.Instance != null)
            {
                SaveDataManager.Instance.OnCharacterCreateResult += OnCharacterCreateResult;
            }

            if (startButton != null) startButton.onClick.AddListener(OnClickStartButton);
            if (createButton != null) createButton.onClick.AddListener(OnClickCreateButton);
            if (deleteButton != null) deleteButton.onClick.AddListener(OnClickDeleteButton);

            RefreshState();
            RefreshCharacterList();
        }

private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveAllListeners();
            if (createButton != null) createButton.onClick.RemoveAllListeners();
            if (deleteButton != null) deleteButton.onClick.RemoveAllListeners();

            if (characterCreatePopup != null)
            {
                characterCreatePopup.OnPopupClosed -= OnCreatePopupClosed;
            }

            if (SaveDataManager.Instance != null)
            {
                SaveDataManager.Instance.OnCharacterCreateResult -= OnCharacterCreateResult;
            }

            ClearListItems();

            if (isPreviewStageDynamicallyCreated && previewStage != null)
            {
                Destroy(previewStage.gameObject);
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// SaveDataManager의 선택 상태(HasSelectedCharacter/SelectedCharacter) 기준으로 버튼 상태,
        /// 닉네임/레벨 표시, 3D 외형 미리보기를 갱신한다. 선택/생성/삭제 직후 다시 호출해줘야 최신 상태가 반영된다.
        /// </summary>
        public void RefreshState()
        {
            bool hasSelectedCharacter = SaveDataManager.Instance != null && SaveDataManager.Instance.HasSelectedCharacter;

            if (startButton != null) startButton.gameObject.SetActive(hasSelectedCharacter);
            if (deleteButton != null) deleteButton.gameObject.SetActive(hasSelectedCharacter);

            if (selectedCharacterTitleText != null)
            {
                CharacterSummary selected = SaveDataManager.Instance != null ? SaveDataManager.Instance.SelectedCharacter : null;
                selectedCharacterTitleText.text = selected != null ? $"{selected.nickname} / Lv.{selected.level}" : "캐릭터 없음";
            }

            RefreshSelectedCharacterPreview();
        }

        /// <summary>
        /// 계정이 보유한 캐릭터 목록을 서버에서 받아와 contentRect 하위에 UI_CharacterListItem을 보유 수만큼 생성해 표시한다.
        /// 로그인 직후 LobbyScene 진입 시(Awake)와 캐릭터 생성 완료/삭제 직후에 호출된다.
        /// </summary>
        public void RefreshCharacterList()
        {
            if (SaveDataManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<UI_LobbySceneView>("SaveDataManager.Instance가 null입니다.");
                return;
            }

            SaveDataManager.Instance.FetchCharacterListAsync(OnCharacterListFetched);
        }

        private void OnCharacterListFetched(List<CharacterSummary> _characters)
        {
            ClearListItems();

            if (_characters != null && contentRect != null && characterListItemPrefab != null)
            {
                for (int i = 0; i < _characters.Count; i++)
                {
                    CharacterSummary character = _characters[i];
                    UI_CharacterListItem item = Instantiate(characterListItemPrefab, contentRect);
                    item.SetData(character);
                    item.SetSelected(SaveDataManager.Instance != null && SaveDataManager.Instance.SelectedCharacterId == character.id);
                    item.OnClicked += OnCharacterItemClicked;
                    spawnedListItems.Add(item);
                }
            }

            RefreshState();
        }

        private void ClearListItems()
        {
            for (int i = 0; i < spawnedListItems.Count; i++)
            {
                if (spawnedListItems[i] != null)
                {
                    spawnedListItems[i].OnClicked -= OnCharacterItemClicked;
                    Destroy(spawnedListItems[i].gameObject);
                }
            }

            spawnedListItems.Clear();
        }

        private void OnCharacterItemClicked(CharacterSummary _character)
        {
            if (SaveDataManager.Instance == null || _character == null)
            {
                return;
            }

            SaveDataManager.Instance.SelectCharacter(_character);

            for (int i = 0; i < spawnedListItems.Count; i++)
            {
                UI_CharacterListItem item = spawnedListItems[i];
                item.SetSelected(item.Character != null && item.Character.id == _character.id);
            }

            RefreshState();
        }

        /// <summary>
        /// 현재 선택된 캐릭터와 미리보기에 실제로 적용된 캐릭터가 다르면, 상세(헤어/눈/입) 데이터를 받아와 3D 모델에 반영한다.
        /// 이미 같은 캐릭터를 표시 중이면(중복 요청 방지) 아무 것도 하지 않는다.
        /// </summary>
private void RefreshSelectedCharacterPreview()
        {
            long? selectedId = SaveDataManager.Instance != null ? SaveDataManager.Instance.SelectedCharacterId : null;

            if (!selectedId.HasValue)
            {
                previewedCharacterId = null;
                pendingPreviewCharacterId = null;
                ClearPreview();
                return;
            }

            if (previewedCharacterId.HasValue && previewedCharacterId.Value == selectedId.Value)
            {
                // 이미 이 캐릭터의 외형이 정확히 반영된 상태.
                return;
            }

            if (pendingPreviewCharacterId.HasValue && pendingPreviewCharacterId.Value == selectedId.Value)
            {
                // 이미 같은 캐릭터를 상세 조회 중 - 중복 요청 방지.
                return;
            }

            // 다른 캐릭터로 전환 중이다. 새 캐릭터의 외형(헤어/눈/입) 응답이 오기 전까지는
            // 선택 해제된(이전) 캐릭터의 PreviewStage가 겹춐 보이지 않도록 즉시 비활성화해 숨긴다.
            pendingPreviewCharacterId = selectedId;
            previewedCharacterId = null;
            ClearPreview();

            SaveDataManager.Instance.FetchCharacterDetailAsync(selectedId.Value, OnSelectedCharacterDetailFetched);
        }

private void OnSelectedCharacterDetailFetched(UserSaveData _data)
        {
            if (_data == null)
            {
                return;
            }

            if (pendingPreviewCharacterId.HasValue && pendingPreviewCharacterId.Value == _data.characterId)
            {
                pendingPreviewCharacterId = null;
            }

            // 응답이 오는 동안 다른 캐릭터로 선택이 바뀌었다면(낮은 응답) 프리뷰를 보여주지 않고 무시한다.
            // 해당 선택은 별도의 RefreshSelectedCharacterPreview 호출로 이미 처리 중이다.
            if (SaveDataManager.Instance == null || SaveDataManager.Instance.SelectedCharacterId != _data.characterId)
            {
                return;
            }

            EnsurePreviewStage();

            if (previewStage != null)
            {
                previewStage.ApplyCustomization(_data.hairIndex, _data.eyeIndex, _data.mouthIndex);
            }

            if (selectedPreviewImage != null)
            {
                selectedPreviewImage.gameObject.SetActive(true);
            }

            previewedCharacterId = _data.characterId;
        }

private const string PreviewStageName = "LobbySelectedCharacterPreviewStage";

        private void EnsurePreviewStage()
        {
            if (previewStage == null)
            {
                // 도메인 리로드/씨 재진입 등으로 previewStage 참조를 잃어버렸더라도, 새로 만들기 전에
                // 씨에 이미 남아있는 동일 이름의 고아 스테이지가 있는지 먼저 찾아 재사용하고,
                // 남아도는(이미 하나 재사용한 뒤의 나머지) 모두 파괴해 캐릭터가 겹쳐 보이는 상황을 방지한다.
                CharacterPreviewStage[] existingStages = FindObjectsByType<CharacterPreviewStage>(FindObjectsSortMode.None);
                for (int i = 0; i < existingStages.Length; i++)
                {
                    CharacterPreviewStage existing = existingStages[i];
                    if (existing == null || existing.gameObject.name != PreviewStageName)
                    {
                        continue;
                    }

                    if (previewStage == null)
                    {
                        previewStage = existing;
                        isPreviewStageDynamicallyCreated = true;
                    }
                    else if (existing != previewStage)
                    {
                        Destroy(existing.gameObject);
                    }
                }
            }

            if (previewStage == null)
            {
                GameObject stageGo = new GameObject(PreviewStageName);
                stageGo.transform.position = dynamicStageSpawnPosition;
                previewStage = stageGo.AddComponent<CharacterPreviewStage>();
                isPreviewStageDynamicallyCreated = true;
            }

            if (selectedPreviewImage != null && selectedPreviewImage.texture == null)
            {
                RenderTexture rt = previewStage.PreviewTexture != null ? previewStage.PreviewTexture : previewStage.SetupPreview(512, 512);
                selectedPreviewImage.texture = rt;
            }
        }

private void ClearPreview()
        {
            if (selectedPreviewImage != null)
            {
                selectedPreviewImage.texture = null;
                selectedPreviewImage.gameObject.SetActive(false);
            }
        }

        private void OnClickStartButton()
        {
            OnStartRequested?.Invoke();
        }

private void OnClickCreateButton()
        {
            // 캐릭터 생성 팝업이 열려 있는 동안에는 로비의 선택된 캐릭터 프리뷰(별도 PreviewStage)가 배경에 계속 넌남아 있으면
            // 팝업 자체의 프리뷰와 두 캐릭터 몸이 동시에 보이는 것처럼 보일 수 있어, 열려있는 동안은 명시적으로 숨긴다.
            if (selectedPreviewImage != null)
            {
                selectedPreviewImage.gameObject.SetActive(false);
            }

            characterCreatePopup?.Open();
        }


        /// <summary>
        /// 캐릭터 생성 팝업이 닫혔을 때 호출된다. OnClickCreateButton에서 숨겼던 로비 선택 프리뷰를,
        /// 여전히 선택된 캐릭터가 있을 때만 다시 보여준다(그 사이 캐릭터가 삭제되어 선택 해제된 경우를 대비).
        /// </summary>
        private void OnCreatePopupClosed()
        {
            if (selectedPreviewImage != null && SaveDataManager.Instance != null && SaveDataManager.Instance.HasSelectedCharacter)
            {
                selectedPreviewImage.gameObject.SetActive(true);
            }
        }


        private void OnClickDeleteButton()
        {
            OnDeleteRequested?.Invoke();
        }

        /// <summary>
        /// SaveDataManager.CreateNew는 낙관적으로 즉시 true를 반환하므로, 실제 목록 갱신은
        /// 서버 응답이 실제로 도착한 이 이벤트(OnCharacterCreateResult)를 기준으로 수행한다.
        /// </summary>
        private void OnCharacterCreateResult(bool _isSuccess)
        {
            if (_isSuccess)
            {
                RefreshCharacterList();
            }
        }
        #endregion
    }
}
