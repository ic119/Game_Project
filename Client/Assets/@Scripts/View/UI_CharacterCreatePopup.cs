using System;
using System.Text.RegularExpressions;
using Incheol.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 생성 팝업 UI View
/// - 3D 캐릭터 실시간 프리뷰 영역 (회전 제어)
/// - 닉네임 입력 및 글자 수/유효성 검사
/// - 캐릭터 외형(머리, 눈, 입) 실시간 커스터마이징 및 3D 모델 연동
/// - 기본 능력치 표시
/// - 캐릭터 저장 및 팝업 닫기/취소 기능
/// </summary>
public class UI_CharacterCreatePopup : MonoBehaviour
{
    #region Variable
    [Header("3D Preview Components")]
    [SerializeField] private RawImage characterPreviewImage;
    [SerializeField] private UI_DragRotateHandler dragRotateHandler;
    [SerializeField] private Button rotateLeftButton;
    [SerializeField] private Button rotateRightButton;
    [SerializeField] private Button resetRotationButton;
    [SerializeField] private TextMeshProUGUI previewNameBadge;
    [SerializeField] private CharacterPreviewStage previewStage;
    [SerializeField] private float rotationStepAngle = 45f;
    [SerializeField] private Vector3 dynamicStageSpawnPosition = new Vector3(500f, 500f, 500f);

    [Header("Nickname Input")]
    [SerializeField] private TMP_InputField nicknameInputField;
    [SerializeField] private TextMeshProUGUI charCountText;
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private int minNicknameLength = 2;
    [SerializeField] private int maxNicknameLength = 12;

    [Header("Customization - Hair")]
    [SerializeField] private Button prevHairButton;
    [SerializeField] private Button nextHairButton;
    [SerializeField] private TextMeshProUGUI hairValueText;

    [Header("Customization - Eye")]
    [SerializeField] private Button prevEyeButton;
    [SerializeField] private Button nextEyeButton;
    [SerializeField] private TextMeshProUGUI eyeValueText;

    [Header("Customization - Mouth")]
    [SerializeField] private Button prevMouthButton;
    [SerializeField] private Button nextMouthButton;
    [SerializeField] private TextMeshProUGUI mouthValueText;

    [Header("Customization - Randomize")]
    [SerializeField] private Button randomizeButton;

    [Header("Stats Display")]
    [SerializeField] private TextMeshProUGUI strValueText;
    [SerializeField] private TextMeshProUGUI agiValueText;
    [SerializeField] private TextMeshProUGUI intValueText;
    [SerializeField] private TextMeshProUGUI totalValueText;

    [Header("Action Buttons")]
    [SerializeField] private Button createButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button closeButton;

    private int selectedHairIndex = 0;
    private int selectedEyeIndex = 0;
    private int selectedMouthIndex = 0;

    private int totalHairCount = 13;
    private int totalEyeCount = 12;
    private int totalMouthCount = 12;

    private UserStats baseUserStats;
    private bool isInitialized = false;
    private bool isPreviewStageDynamicallyCreated = false;
    private Action<float> onRotateDeltaHandler;

    private static readonly Regex ValidNicknamePattern = new Regex(@"^[a-zA-Z0-9가-힣]+$");

    public event Action<UserSaveData> OnCharacterCreated;
    public event Action OnPopupClosed;

    public CharacterPreviewStage PreviewStage => previewStage;

    /// <summary>
    /// 실제 저장/게임 상태 갱신을 담당하는 상위 Controller가 주입하는 처리기.
    /// 성공 시 true, 실패 시 false를 반환해야 함.
    /// </summary>
    public Func<UserSaveData, bool> OnCreateRequested;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        InitializeComponents();
    }

    private void OnEnable()
    {
        ResetForm();
        if (previewStage != null && characterPreviewImage != null)
        {
            var rt = previewStage.PreviewTexture != null ? previewStage.PreviewTexture : previewStage.SetupPreview(1024, 1024);
            characterPreviewImage.texture = rt;
            previewStage.ApplyCustomization(selectedHairIndex, selectedEyeIndex, selectedMouthIndex);
        }
    }

    private void OnDestroy()
    {
        UnregisterEvents();

        if (previewStage != null)
        {
            previewStage.OnCharacterModelReady -= HandleCharacterModelReady;
        }

        if (isPreviewStageDynamicallyCreated && previewStage != null)
        {
            Destroy(previewStage.gameObject);
        }
    }
    #endregion

    #region Method
public void InitializeComponents()
    {
        if (isInitialized) return;
        isInitialized = true;

        // Create/Cancel/닉네임 등 핵심 버튼 배선(RegisterEvents)은 3D 프리뷰 설정보다 먼저, 독립적으로 실행되어야 한다.
        // 프리뷰 스테이지 초기화(카메라/렌더텍스처/머트리얼 생성 등)가 예외로 실패해도
        // 케릭터 생성 자체는 계속 동작해야 한다 — 이전에는 RegisterEvents가 이 메서드 끝에 있어서,
        // 위 초기화 중 하나라도 예외가 나면 Create 버튼이 영원히 작동하지 않는 문제가 있었다.
        RegisterEvents();

        // 기본 능력치 데이터 세팅
        baseUserStats = UserStats.CreateDefault();

        UpdateStatsUI();

        try
        {
            InitializePreviewStage();
        }
        catch (Exception exception)
        {
            DebugLogManager.GenerateErrorMessage<UI_CharacterCreatePopup>(
                $"3D 프리뷰 스테이지 초기화 중 예외가 발생했습니다(캐릭터 생성 자체는 계속 진행 가능) : {exception}");
        }

        UpdateCountsFromStage();
        ApplyCustomizationToPreview();
    }

    /// <summary>
    /// 3D 캐릭터 미리보기 스테이지를 동적 생성/연결하고 렌더텍스처를 준비한다.
    /// 카메라/메쉬/셔이더 관련 예외가 여기서 나도 상위 InitializeComponents의 핵심 버튼 배선에는 영향을 주지 않는다.
    /// </summary>
    private void InitializePreviewStage()
    {
        if (previewStage == null)
        {
            var stageGo = new GameObject("CharacterPreviewStage");
            stageGo.transform.position = dynamicStageSpawnPosition;
            previewStage = stageGo.AddComponent<CharacterPreviewStage>();
            isPreviewStageDynamicallyCreated = true;
        }

        if (characterPreviewImage != null && previewStage != null)
        {
            var rt = previewStage.SetupPreview(1024, 1024);
            characterPreviewImage.texture = rt;
        }

        if (previewStage != null)
        {
            previewStage.OnCharacterModelReady += HandleCharacterModelReady;
        }
    }

    /// <summary>
    /// 캐릭터 모델이 Addressable 비동기 로드로 늦게 준비된 경우, 실제 파츠 개수와
    /// 현재 선택된 외형을 다시 동기화한다.
    /// </summary>
    private void HandleCharacterModelReady()
    {
        UpdateCountsFromStage();
        ApplyCustomizationToPreview();
    }

    private void UpdateCountsFromStage()
    {
        if (previewStage != null)
        {
            if (previewStage.HairCount > 0) totalHairCount = previewStage.HairCount;
            if (previewStage.EyeCount > 0) totalEyeCount = previewStage.EyeCount;
            if (previewStage.MouthCount > 0) totalMouthCount = previewStage.MouthCount;
        }
    }

    private void RegisterEvents()
    {
        if (nicknameInputField != null)
        {
            nicknameInputField.characterLimit = maxNicknameLength;
            nicknameInputField.onValueChanged.AddListener(OnNicknameValueChanged);
            nicknameInputField.onValidateInput += ValidateNicknameCharacter;
        }

        // Hair Customization
        if (prevHairButton != null)
        {
            prevHairButton.onClick.AddListener(() => ChangeHair(-1));
        }
        if (nextHairButton != null)
        {
            nextHairButton.onClick.AddListener(() => ChangeHair(1));
        }

        // Eye Customization
        if (prevEyeButton != null)
        {
            prevEyeButton.onClick.AddListener(() => ChangeEye(-1));
        }
        if (nextEyeButton != null)
        {
            nextEyeButton.onClick.AddListener(() => ChangeEye(1));
        }

        // Mouth Customization
        if (prevMouthButton != null)
        {
            prevMouthButton.onClick.AddListener(() => ChangeMouth(-1));
        }
        if (nextMouthButton != null)
        {
            nextMouthButton.onClick.AddListener(() => ChangeMouth(1));
        }

        // Randomize
        if (randomizeButton != null)
        {
            randomizeButton.onClick.AddListener(RandomizeAppearance);
        }

        // Rotation controls
        if (rotateLeftButton != null)
        {
            rotateLeftButton.onClick.AddListener(() => previewStage?.RotateLeft(rotationStepAngle));
        }

        if (rotateRightButton != null)
        {
            rotateRightButton.onClick.AddListener(() => previewStage?.RotateRight(rotationStepAngle));
        }

        if (resetRotationButton != null)
        {
            resetRotationButton.onClick.AddListener(() => previewStage?.ResetRotation());
        }

        if (dragRotateHandler != null)
        {
            onRotateDeltaHandler = (delta) => previewStage?.AddRotation(delta);
            dragRotateHandler.OnRotateDelta += onRotateDeltaHandler;
        }

        if (createButton != null)
        {
            createButton.onClick.AddListener(OnClickCreateButton);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(ClosePopup);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePopup);
        }
    }

    private void UnregisterEvents()
    {
        if (nicknameInputField != null) nicknameInputField.onValueChanged.RemoveAllListeners();
        if (nicknameInputField != null) nicknameInputField.onValidateInput -= ValidateNicknameCharacter;
        if (prevHairButton != null) prevHairButton.onClick.RemoveAllListeners();
        if (nextHairButton != null) nextHairButton.onClick.RemoveAllListeners();
        if (prevEyeButton != null) prevEyeButton.onClick.RemoveAllListeners();
        if (nextEyeButton != null) nextEyeButton.onClick.RemoveAllListeners();
        if (prevMouthButton != null) prevMouthButton.onClick.RemoveAllListeners();
        if (nextMouthButton != null) nextMouthButton.onClick.RemoveAllListeners();
        if (randomizeButton != null) randomizeButton.onClick.RemoveAllListeners();
        if (rotateLeftButton != null) rotateLeftButton.onClick.RemoveAllListeners();
        if (rotateRightButton != null) rotateRightButton.onClick.RemoveAllListeners();
        if (resetRotationButton != null) resetRotationButton.onClick.RemoveAllListeners();
        if (createButton != null) createButton.onClick.RemoveAllListeners();
        if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
        if (closeButton != null) closeButton.onClick.RemoveAllListeners();

        if (dragRotateHandler != null && onRotateDeltaHandler != null)
        {
            dragRotateHandler.OnRotateDelta -= onRotateDeltaHandler;
            onRotateDeltaHandler = null;
        }
    }

    public void Open()
    {
        gameObject.SetActive(true);
        ResetForm();
    }

    public void ClosePopup()
    {
        gameObject.SetActive(false);
        OnPopupClosed?.Invoke();
    }

    private void ResetForm()
    {
        if (nicknameInputField != null)
        {
            nicknameInputField.text = string.Empty;
        }

        selectedHairIndex = 0;
        selectedEyeIndex = 0;
        selectedMouthIndex = 0;

        UpdateCountsFromStage();
        UpdateCustomizationUI();
        ApplyCustomizationToPreview();
        UpdateNicknameValidation(string.Empty);

        if (previewStage != null)
        {
            previewStage.ResetRotation();
        }
    }

    #region Customization Logic
    public void ChangeHair(int delta)
    {
        UpdateCountsFromStage();
        selectedHairIndex = (selectedHairIndex + delta + totalHairCount) % totalHairCount;
        UpdateHairUI();
        if (previewStage != null)
        {
            previewStage.SetHair(selectedHairIndex);
        }
    }

    public void ChangeEye(int delta)
    {
        UpdateCountsFromStage();
        selectedEyeIndex = (selectedEyeIndex + delta + totalEyeCount) % totalEyeCount;
        UpdateEyeUI();
        if (previewStage != null)
        {
            previewStage.SetEye(selectedEyeIndex);
        }
    }

    public void ChangeMouth(int delta)
    {
        UpdateCountsFromStage();
        selectedMouthIndex = (selectedMouthIndex + delta + totalMouthCount) % totalMouthCount;
        UpdateMouthUI();
        if (previewStage != null)
        {
            previewStage.SetMouth(selectedMouthIndex);
        }
    }

    public void RandomizeAppearance()
    {
        UpdateCountsFromStage();
        selectedHairIndex = UnityEngine.Random.Range(0, totalHairCount);
        selectedEyeIndex = UnityEngine.Random.Range(0, totalEyeCount);
        selectedMouthIndex = UnityEngine.Random.Range(0, totalMouthCount);
        UpdateCustomizationUI();
        ApplyCustomizationToPreview();
    }

    private void ApplyCustomizationToPreview()
    {
        if (previewStage != null)
        {
            previewStage.ApplyCustomization(selectedHairIndex, selectedEyeIndex, selectedMouthIndex);
        }
        UpdateCustomizationUI();
    }

    private void UpdateCustomizationUI()
    {
        UpdateHairUI();
        UpdateEyeUI();
        UpdateMouthUI();
    }

    private void UpdateHairUI()
    {
        if (hairValueText != null)
        {
            hairValueText.text = $"스타일 {selectedHairIndex + 1:D2} / {totalHairCount:D2}";
        }
    }

    private void UpdateEyeUI()
    {
        if (eyeValueText != null)
        {
            eyeValueText.text = $"스타일 {selectedEyeIndex + 1:D2} / {totalEyeCount:D2}";
        }
    }

    private void UpdateMouthUI()
    {
        if (mouthValueText != null)
        {
            mouthValueText.text = $"스타일 {selectedMouthIndex + 1:D2} / {totalMouthCount:D2}";
        }
    }
    #endregion

    /// <summary>
    /// 닉네임 입력 필드에 한글/영문/숫자 외의 문자(공백, 특수문자 등)가 아예 입력되지 않도록 문자 단위로 막는다.
    /// 제출 시 최종 검증(UpdateNicknameValidation)에서도 같은 ValidNicknamePattern을 쓰므로, 두 검증 기준이 서로 어긋나지 않는다.
    /// IME(한글 조합) 입력 중간에 완성되지 않은 자모가 들어오는 극드물러운 경우가 있을 수 있으니,
    /// 만약 한글 입력이 막히는 현상이 발견되면 이 메서드를 제거하고 제출 시 검증만으로 되돌려야 한다.
    /// </summary>
    private char ValidateNicknameCharacter(string text, int charIndex, char addedChar)
    {
        return ValidNicknamePattern.IsMatch(addedChar.ToString()) ? addedChar : '\0';
    }

    private void OnNicknameValueChanged(string text)
    {
        UpdateNicknameValidation(text);

        if (previewNameBadge != null)
        {
            previewNameBadge.text = string.IsNullOrWhiteSpace(text) ? "이름 없음" : text;
        }
    }

    private bool UpdateNicknameValidation(string text)
    {
        int length = text != null ? text.Trim().Length : 0;

        if (charCountText != null)
        {
            charCountText.text = $"{length} / {maxNicknameLength}";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            SetFeedback("닉네임을 입력해 주세요.", new Color(0.8f, 0.8f, 0.8f, 0.8f));
            SetCreateButtonInteractable(false);
            return false;
        }

        if (length < minNicknameLength)
        {
            SetFeedback($"닉네임은 최소 {minNicknameLength}자 이상이어야 합니다.", new Color(1f, 0.4f, 0.4f, 1f));
            SetCreateButtonInteractable(false);
            return false;
        }

        if (!ValidNicknamePattern.IsMatch(text.Trim()))
        {
            SetFeedback("닉네임은 한글, 영문, 숫자만 사용할 수 있습니다.", new Color(1f, 0.4f, 0.4f, 1f));
            SetCreateButtonInteractable(false);
            return false;
        }

        SetFeedback("사용 가능한 닉네임입니다.", new Color(0.3f, 0.9f, 0.5f, 1f));
        SetCreateButtonInteractable(true);
        return true;
    }

    private void SetFeedback(string message, Color color)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }
    }

    private void SetCreateButtonInteractable(bool interactable)
    {
        if (createButton != null)
        {
            createButton.interactable = interactable;
        }
    }

    private void UpdateStatsUI()
    {
        if (baseUserStats == null) return;

        if (strValueText != null) strValueText.text = baseUserStats.str.ToString();
        if (agiValueText != null) agiValueText.text = baseUserStats.agi.ToString();
        if (intValueText != null) intValueText.text = baseUserStats.intel.ToString();
        if (totalValueText != null) totalValueText.text = baseUserStats.GetTotal().ToString();
    }

    /// <summary>
    /// 저장에 사용할 외형 인덱스를 CharacterCustomModel(실제 적용된 모델)에서 읽어온다.
    /// Addressable 로드가 아직 안 끝나 모델이 없는 예외적인 경우에만 UI가 추적 중인 선택값으로 대체한다.
    /// </summary>
    private void ResolveCurrentCustomizationIndices(out int hairIndex, out int eyeIndex, out int mouthIndex)
    {
        var customModel = previewStage != null ? previewStage.CustomModel : null;

        hairIndex = customModel != null ? customModel.CurrentHairIndex : selectedHairIndex;
        eyeIndex = customModel != null ? customModel.CurrentEyeIndex : selectedEyeIndex;
        mouthIndex = customModel != null ? customModel.CurrentMouthIndex : selectedMouthIndex;
    }

    private void OnClickCreateButton()
    {
        string nickname = nicknameInputField != null ? nicknameInputField.text.Trim() : string.Empty;

        if (!UpdateNicknameValidation(nickname))
        {
            return;
        }

        // 저장할 외형 값은 UI가 따로 들고 있는 selected*Index가 아니라, 실제로 화면에 적용된
        // CharacterCustomModel의 현재 상태를 소스 오브 트루스로 사용한다.
        ResolveCurrentCustomizationIndices(out int hairIndex, out int eyeIndex, out int mouthIndex);

        // 유저 세이브 데이터 구성 (UI 입력값만 담당 - 저장/게임 상태 처리는 상위 Controller가 담당)
        var userSaveData = UserSaveData.CreateDefault(nickname, hairIndex, eyeIndex, mouthIndex);;

        if (OnCreateRequested == null)
        {
            DebugLogManager.GenerateErrorMessage<UI_CharacterCreatePopup>(
                $"OnCreateRequested가 null입니다. popup InstanceID={GetInstanceID()}. LobbySceneController.WireCharacterCreatePopup 로그의 InstanceID와 비교해주세요.");
            SetFeedback("저장 처리기가 연결되지 않아 저장에 실패했습니다.", new Color(1f, 0.4f, 0.4f, 1f));
            return;
        }

        bool saveSuccess = OnCreateRequested.Invoke(userSaveData);
        if (!saveSuccess)
        {
            SetFeedback("캐릭터 저장에 실패했습니다. 다시 시도해 주세요.", new Color(1f, 0.4f, 0.4f, 1f));
            return;
        }

        OnCharacterCreated?.Invoke(userSaveData);
        ClosePopup();
    }
    #endregion
}
