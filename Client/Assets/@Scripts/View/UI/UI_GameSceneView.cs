using System;
using Incheol.Controller;
using Incheol.Utils;
using Incheol.View.UI;
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
    [SerializeField] private TextMeshProUGUI hpBarSliderValue;

    [Header("마나 UI")]
    [Tooltip("마나 바. 체력 바와 같은 방식으로 PlayerCharacterModel의 현재/최대 마나를 폴링해 갱신한다. 비워 두면 마나 표시를 건너뛴다.")]
    [SerializeField] private Slider mpBarSlider;
    [SerializeField] private TextMeshProUGUI mpBarSliderValue;

    [Header("경험치 UI")]
    [SerializeField] private Slider expBarSlider;
    [SerializeField] private TextMeshProUGUI expBarSliderValue;

    [Header("스킬 바")]
    [Tooltip("BottomContainer/SkillContainer의 UI_SkillBarView. GameSceneManager가 로컬 플레이어의 PlayerSkillController를 연결한다. 비워 두면 스킬 바 표시를 건너뛴다.")]
    [SerializeField] private UI_SkillBarView skillBarView;

    [Header("Menu Button UI")]
    [SerializeField] private GameObject mainPopup;
    [SerializeField] private Button optionButton;
    [SerializeField] private Button menuButton;
    [SerializeField] private Button logoutButton;
    [SerializeField] private Button cancelButton;

    [Header("Mini Map")]
    [SerializeField] private RawImage miniMapView;
    [SerializeField] private Sprite playerMiniMapIcon;

    [Header("Session Kicked Popup")]
    [Tooltip("같은 캐릭터로 다른 곳에서 접속해 이 접속이 끊겼을 때 띄울 팝업(UI_SessionKickedPopupView). 비워두면 공용 알림 팝업으로 대신한다.")]
    [SerializeField] private UI_SessionKickedPopupView sessionKickedPopup;

    private PlayerCharacterModel playerModel;

    // 레벨 라벨에 마지막으로 표시한 레벨. 서버 경험치 패킷으로 레벨이 바뀌어도 라벨이 갱신되도록 Update에서 비교한다(-1: 아직 표시 전).
    private int displayedLevel = -1;
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
    }

    private void OnDisable()
    {
        menuButton.onClick.RemoveListener(OnClickMenuButton);
        logoutButton.onClick.RemoveListener(OnClickLogoutButton);

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(OnClickCancelButton);
        }
    }

    /// <summary>
    /// 체력/경험치는 전투 중 실시간으로 바뀌므로 이벤트 대신 폴링으로 슬라이더를 갱신한다
    /// (PlayerCharacterModel 주석에 명시된 기존 컨벤션). 레벨도 같은 이유로 폴링하되, 값이 바뀐 프레임에만
    /// 문자열을 다시 만든다(RefreshLevelLabel).
    /// </summary>
    private void Update()
    {
        if (playerModel == null)
        {
            return;
        }

        if (playerModel.Level != displayedLevel)
        {
            RefreshLevelLabel();
        }

        if (hpBarSlider != null)
        {
            hpBarSlider.maxValue = Mathf.Max(1, playerModel.MaxHp);
            hpBarSlider.value = playerModel.CurrentHp;
        }

        if (hpBarSliderValue != null)
        {
            hpBarSliderValue.text = $"{playerModel.CurrentHp}/{playerModel.MaxHp}";
        }

        if (mpBarSlider != null)
        {
            mpBarSlider.maxValue = Mathf.Max(1, playerModel.MaxMp);
            mpBarSlider.value = playerModel.CurrentMp;
        }

        if (mpBarSliderValue != null)
        {
            mpBarSliderValue.text = $"{playerModel.CurrentMp}/{playerModel.MaxMp}";
        }

        if (expBarSlider != null)
        {
            // 만렙(ExpToNextLevel == 0)이면 슬라이더를 가득 채운 상태로 고정한다.
            expBarSlider.maxValue = Mathf.Max(1, playerModel.ExpToNextLevel);
            expBarSlider.value = playerModel.ExpToNextLevel > 0 ? playerModel.CurrentExp : expBarSlider.maxValue;
        }

        if (expBarSliderValue != null)
        {
            expBarSliderValue.text = FormatExpText(playerModel.CurrentExp, playerModel.ExpToNextLevel);
        }
    }
    #endregion

    #region Method
    public event Action MenuButtonClicked;
    public event Action LogoutButtonClicked;

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

        RefreshLevelLabel();
    }

    private void RefreshLevelLabel()
    {
        displayedLevel = playerModel.Level;

        if (playerLevelLabel != null)
        {
            playerLevelLabel.text = $"Lv.{displayedLevel}";
        }
    }

    /// <summary>
    /// "현재/다음 레벨업까지 필요 경험치 (퍼센트%)" 형식으로 표시한다(예: "10/100 (10%)").
    /// 만렙(expToNextLevel이 0)이면 퍼센트 계산이 불가능하므로 "(MAX)"로 표시한다.
    /// </summary>
    private static string FormatExpText(int currentExp, int expToNextLevel)
    {
        if (expToNextLevel <= 0)
        {
            return $"{currentExp} (MAX)";
        }

        int percent = Mathf.RoundToInt((float)currentExp / expToNextLevel * 100f);
        return $"{currentExp}/{expToNextLevel} ({percent}%)";
    }

    /// <summary>
    /// 미니맵을 실제로 그리는 MiniMapController(Presenter가 생성/초기화)가 참조할 RawImage.
    /// </summary>
    public RawImage MiniMapView => miniMapView;

    /// <summary>
    /// MiniMapController가 플레이어 위치 아이콘을 만들 때 사용할 스프라이트.
    /// </summary>
    public Sprite PlayerMiniMapIcon => playerMiniMapIcon;

    /// <summary>
    /// 스킬 슬롯 4개를 묶은 스킬 바. 인스펙터에 연결하지 않았으면 null.
    /// </summary>
    public UI_SkillBarView SkillBarView => skillBarView;

    /// <summary>
    /// 서버가 이 접속을 강제로 끊었을 때 GameSceneManager가 띄울 팝업. 인스펙터에 연결하지 않았으면 null.
    /// </summary>
    public UI_SessionKickedPopupView SessionKickedPopup => sessionKickedPopup;

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
    #endregion
}
