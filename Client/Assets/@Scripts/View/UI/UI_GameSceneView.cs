using System;
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
    }

    private void OnDisable()
    {
        menuButton.onClick.RemoveListener(OnClickMenuButton);
        logoutButton.onClick.RemoveListener(OnClickLogoutButton);
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
        MenuButtonClicked?.Invoke();
    }

    private void OnClickLogoutButton()
    {
        LogoutButtonClicked?.Invoke();
    }
    #endregion
}
