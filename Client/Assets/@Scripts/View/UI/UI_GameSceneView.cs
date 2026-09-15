using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_GameSceneView : MonoBehaviour
{
    #region Variable
    [Header("캐릭터 정보 UI")]
    [SerializeField] private TextMeshProUGUI playerNameLabel;
    [SerializeField] private TextMeshProUGUI playerLevelLabel;
    [SerializeField] private Slider hpBarSlider;
    [SerializeField] private Slider expBarSlider;

    [Header("Menu Button UI")]
    [SerializeField] private GameObject mainPopup;
    [SerializeField] private Button optionButton;
    [SerializeField] private Button menuButton;
    [SerializeField] private Button logoutButton;
    [SerializeField] private Button cancelButton;

    [Header("Chat UI")]
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private RectTransform chatContentRoot;
    [SerializeField] private GameObject chatMessageTemplate;

    /// <summary>
    /// 채팅창에 쌓아두는 메시지 아이템의 최대 개수. 세션이 길어져도 UI 오브젝트가 무한히 늘어나지 않도록
    /// 오래된 메시지부터 제거한다.
    /// </summary>
    private const int MaxChatMessageCount = 100;

    private readonly Queue<GameObject> chatMessageInstances = new();

    /// <summary>
    /// 경험치 슬라이더의 만렙치 기준값. 서버에 경험치가 아직 저장되지 않아 실제 "다음 레벨까지 필요한 경험치"
    /// 개념이 없으므로 임시로 고정값을 쓴다 - CombatStatComponent.ApplyFromUserStats와 같은 성격의 임시값이며,
    /// 실제 레벨링 기획이 정해지면 이 값(또는 계산식)만 바꾸면 된다.
    /// </summary>
    private const float PlaceholderMaxExp = 100f;

    private PlayerCharacterModel playerModel;
    #endregion

    #region LifeCycle
    private void OnEnable()
    {
        menuButton.onClick.AddListener(OnClickMenuButton);
        logoutButton.onClick.AddListener(OnClickLogoutButton);

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(OnClickCancelButton);
        }

        SetMainPopupActive(false);

        if (chatInputField != null)
        {
            chatInputField.onSubmit.AddListener(OnChatInputSubmit);
        }
    }

    private void OnDisable()
    {
        menuButton.onClick.RemoveListener(OnClickMenuButton);
        logoutButton.onClick.RemoveListener(OnClickLogoutButton);

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(OnClickCancelButton);
        }

        if (chatInputField != null)
        {
            chatInputField.onSubmit.RemoveListener(OnChatInputSubmit);
        }
    }

    /// <summary>
    /// 체력/경험치는 전투 중 실시간으로 바뀌므로 이벤트 대신 폴링으로 슬라이더를 갱신한다
    /// (PlayerCharacterModel 주석에 명시된 기존 컨벤션).
    /// </summary>
    private void Update()
    {
        if (playerModel == null)
        {
            return;
        }

        if (hpBarSlider != null)
        {
            hpBarSlider.maxValue = Mathf.Max(1, playerModel.MaxHp);
            hpBarSlider.value = playerModel.CurrentHp;
        }

        if (expBarSlider != null)
        {
            expBarSlider.maxValue = PlaceholderMaxExp;
            expBarSlider.value = playerModel.CurrentExp;
        }
    }
    #endregion

    #region Method
    public event Action MenuButtonClicked;
    public event Action LogoutButtonClicked;
    public event Action<string> ChatMessageSubmitted;

    /// <summary>
    /// 스폰된 로컬 플레이어를 이 뷰에 연결한다. 닉네임/레벨은 즉시 표시하고,
    /// 체력/경험치는 Update()에서 계속 폴링해 슬라이더를 갱신한다.
    /// </summary>
    public void BindPlayer(PlayerCharacterModel _playerModel)
    {
        playerModel = _playerModel;

        if (playerModel == null)
        {
            return;
        }

        if (playerNameLabel != null)
        {
            playerNameLabel.text = playerModel.Nickname;
        }

        if (playerLevelLabel != null)
        {
            playerLevelLabel.text = $"Lv.{playerModel.Level}";
        }
    }

    public bool IsMainPopupActive => mainPopup != null && mainPopup.activeSelf;

    public void SetMainPopupActive(bool _isActive)
    {
        if (mainPopup != null)
        {
            mainPopup.SetActive(_isActive);
        }
    }

    private void OnClickMenuButton()
    {
        SetMainPopupActive(true);
        MenuButtonClicked?.Invoke();
    }

    private void OnClickLogoutButton()
    {
        LogoutButtonClicked?.Invoke();
    }

    /// <summary>
    /// mainPopup이 켜져 있을 때만 끈다(이미 꺼져 있으면 아무 것도 하지 않음).
    /// </summary>
    private void OnClickCancelButton()
    {
        if (IsMainPopupActive)
        {
            SetMainPopupActive(false);
        }
    }

    /// <summary>
    /// ChatContainer/Scroll View/Viewport/Content 아래에 chatMessageTemplate을 복제해 한 줄을 추가한다.
    /// MaxChatMessageCount를 넘으면 가장 오래된 항목부터 제거하고, 추가 직후 스크롤을 맨 아래로 내린다.
    /// 리치 텍스트는 채팅 내용에 꺾쇠 문자가 섞여 들어와도 태그로 해석되지 않도록 항상 꺼둔다.
    /// </summary>
    public void AddChatMessage(string _nickname, string _message)
    {
        if (chatMessageTemplate == null || chatContentRoot == null)
        {
            return;
        }

        GameObject instance = Instantiate(chatMessageTemplate, chatContentRoot);
        instance.SetActive(true);

        if (instance.TryGetComponent(out TextMeshProUGUI text))
        {
            text.richText = false;
            text.text = string.IsNullOrEmpty(_nickname) ? _message : $"[{_nickname}]: {_message}";
        }

        chatMessageInstances.Enqueue(instance);

        while (chatMessageInstances.Count > MaxChatMessageCount)
        {
            GameObject oldest = chatMessageInstances.Dequeue();
            if (oldest != null)
            {
                Destroy(oldest);
            }
        }

        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    /// <summary>
    /// ChatInputField에서 Enter(Submit)를 누르면 호출된다. 빈 문자열은 무시하고,
    /// 전송 후에는 입력창을 비우고 즉시 재포커스해 연속으로 대화를 이어갈 수 있게 한다.
    /// </summary>
    private void OnChatInputSubmit(string _text)
    {
        if (string.IsNullOrWhiteSpace(_text))
        {
            return;
        }

        ChatMessageSubmitted?.Invoke(_text.Trim());

        chatInputField.text = string.Empty;
        chatInputField.ActivateInputField();
    }
    #endregion
}
