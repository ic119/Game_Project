using System;
using UnityEngine;
using Incheol.Modules;
using Incheol.Utils;
using Incheol.Models.Define;

/// <summary>
/// 3D 캐릭터 실시간 프리뷰를 위한 스테이지, 카메라, 라이팅 및 커스터마이징 관리 클래스
/// </summary>
public class CharacterPreviewStage : MonoBehaviour
{
    #region Variable
    [Header("Preview Camera & Lighting")]
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Light keyLight;
    [SerializeField] private Light fillLight;
    [SerializeField] private Transform characterRoot;
    [SerializeField] private Transform stagePedestal;

    [Header("Character Model & Customization")]
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private GameObject characterModelInstance;
    [SerializeField] private CharacterCustomModel customModel;

    [Header("Rotation Settings")]
    [SerializeField] private float rotationSensitivity = 0.5f;
    [SerializeField] private float autoRotationSpeed = 10f;
    [SerializeField] private bool autoRotate = false;

    /// <summary>
    /// 프리뷰 카메라가 이 레이어만 렌더링하도록 격리한다. 스테이지가 월드상 먼 좌표에 배치되는 것과
    /// 별개로, 카메라 컬링마스크 자체를 좁혀서 다른 오브젝트가 우연히 프리뷰에 찍히는 걸 막는다.
    /// </summary>
    private const string PreviewLayerName = "CharacterPreview";

    private RenderTexture previewRenderTexture;
    private float currentYaw = 180f;
    private float targetYaw = 180f;
    private bool isRequestingCharacterModel = false;

    private int desiredHairIndex = 0;
    private int desiredEyeIndex = 0;
    private int desiredMouthIndex = 0;

    public RenderTexture PreviewTexture => previewRenderTexture;
    public CharacterCustomModel CustomModel => customModel;

    public int HairCount => customModel != null ? customModel.HairCount : 13;
    public int EyeCount => customModel != null ? customModel.EyeCount : 12;
    public int MouthCount => customModel != null ? customModel.MouthCount : 12;

    public int CurrentHairIndex => customModel != null ? customModel.CurrentHairIndex : 0;
    public int CurrentEyeIndex => customModel != null ? customModel.CurrentEyeIndex : 0;
    public int CurrentMouthIndex => customModel != null ? customModel.CurrentMouthIndex : 0;

    /// <summary>
    /// 캐릭터 모델이 (동기/비동기 로드 어느 쪽이든) 실제로 준비되었을 때 발생.
    /// Addressable 로드가 늦게 끝나는 경우를 대비해, 구독자는 이 시점에 파츠 개수/외형을 다시 동기화해야 한다.
    /// </summary>
    public event Action OnCharacterModelReady;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        InitializeStage();
    }

    private void Update()
    {
        if (autoRotate)
        {
            targetYaw += autoRotationSpeed * Time.deltaTime;
        }

        currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * 15f);

        if (characterRoot != null)
        {
            characterRoot.localRotation = Quaternion.Euler(0f, currentYaw, 0f);
        }
    }

    private void OnDestroy()
    {
        CleanupRenderTexture();
    }
    #endregion

    #region Method
    /// <summary>
    /// 프리뷰 스테이지 및 렌더 텍스처 초기화
    /// </summary>
    public RenderTexture SetupPreview(int width = 1024, int height = 1024)
    {
        InitializeStage();

        if (previewRenderTexture != null)
        {
            CleanupRenderTexture();
        }

        previewRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "CharacterPreview_RenderTexture",
            antiAliasing = 4,
            useMipMap = false,
            autoGenerateMips = false
        };
        previewRenderTexture.Create();

        if (previewCamera != null)
        {
            previewCamera.targetTexture = previewRenderTexture;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0f);
        }

        ResetRotation();
        return previewRenderTexture;
    }

    public void InitializeStage()
    {
        if (previewCamera == null)
        {
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.05f, 2.3f);
            camGo.transform.localRotation = Quaternion.Euler(3f, 180f, 0f);
            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0f);
            previewCamera.fieldOfView = 34f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 20f;
        }

        if (characterRoot == null)
        {
            var rootGo = new GameObject("CharacterRoot");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.localPosition = Vector3.zero;
            characterRoot = rootGo.transform;
        }

        EnsureCharacterModel();
        EnsureLights();
        EnsurePedestal();

        ApplyPreviewLayer(gameObject);
    }

    private void EnsureCharacterModel()
    {
        if (characterModelInstance != null)
        {
            if (customModel == null)
            {
                customModel = characterModelInstance.GetComponent<CharacterCustomModel>();
            }
            return;
        }

        if (characterPrefab != null)
        {
            InstantiateCharacterModel(characterPrefab);
            return;
        }

        if (isRequestingCharacterModel)
        {
            return;
        }

        if (AddressableAssetManager.Instance == null)
        {
            DebugLogManager.GenerateErrorMessage<CharacterPreviewStage>("AddressableAssetManager.Instance가 없어 캐릭터 프리뷰 모델을 로드할 수 없습니다.");
            return;
        }

        isRequestingCharacterModel = true;
        string key = AddressableAssetKey.BasicCharacter.ToString();

        AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(key, prefab =>
        {
            isRequestingCharacterModel = false;

            if (prefab == null)
            {
                DebugLogManager.GenerateErrorMessage<CharacterPreviewStage>($"캐릭터 프리뷰 모델 로드 실패 Key : {key}");
                return;
            }

            characterPrefab = prefab;

            if (characterModelInstance == null)
            {
                InstantiateCharacterModel(prefab);
            }
        });
    }

    /// <summary>
    /// 동기(Inspector 직접 연결) / 비동기(Addressable 로드 완료) 양쪽 경로에서 공통으로 사용하는
    /// 캐릭터 모델 인스턴스화 처리. 생성 직후 대기 중이던 커스터마이징 값을 바로 적용한다.
    /// </summary>
    private void InstantiateCharacterModel(GameObject prefab)
    {
        var instantiated = AddressableAssetManager.Instance != null
            ? AddressableAssetManager.Instance.InstantiatePrefab(prefab)
            : Instantiate(prefab);

        characterModelInstance = instantiated;
        characterModelInstance.transform.SetParent(characterRoot, false);
        characterModelInstance.name = "BasicCharacter_Preview";
        characterModelInstance.transform.localPosition = Vector3.zero;
        characterModelInstance.transform.localRotation = Quaternion.identity;
        customModel = characterModelInstance.GetComponent<CharacterCustomModel>();

        ApplyPreviewLayer(characterModelInstance);
        customModel?.ApplyCustomization(desiredHairIndex, desiredEyeIndex, desiredMouthIndex);

        OnCharacterModelReady?.Invoke();
    }

    /// <summary>
    /// 프리뷰 카메라가 CharacterPreview 레이어만 렌더링하도록 컬링마스크를 좁히고,
    /// 대상 오브젝트 하위 전체를 그 레이어로 옮긴다. 스테이지가 먼 좌표에 격리되는 것과 별개로,
    /// 다른 오브젝트가 카메라 프러스텀에 우연히 겹쳐도 프리뷰에 찍히지 않게 하기 위함이다.
    /// </summary>
    private void ApplyPreviewLayer(GameObject target)
    {
        if (target == null) return;

        int previewLayer = LayerMask.NameToLayer(PreviewLayerName);
        if (previewLayer < 0)
        {
            DebugLogManager.GenerateErrorMessage<CharacterPreviewStage>(
                $"레이어 '{PreviewLayerName}'를 찾을 수 없습니다. Project Settings > Tags and Layers에서 추가해주세요.");
            return;
        }

        SetLayerRecursively(target, previewLayer);

        if (previewCamera != null)
        {
            previewCamera.cullingMask = 1 << previewLayer;
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void EnsureLights()
    {
        if (keyLight == null)
        {
            var keyGo = new GameObject("PreviewKeyLight");
            keyGo.transform.SetParent(transform, false);
            keyGo.transform.localPosition = new Vector3(1.5f, 3f, 2f);
            keyGo.transform.localRotation = Quaternion.Euler(45f, -140f, 0f);
            keyLight = keyGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.3f;
            keyLight.color = new Color(1f, 0.96f, 0.92f);
        }

        if (fillLight == null)
        {
            var fillGo = new GameObject("PreviewFillLight");
            fillGo.transform.SetParent(transform, false);
            fillGo.transform.localPosition = new Vector3(-2f, 1.5f, -1f);
            fillGo.transform.localRotation = Quaternion.Euler(20f, 40f, 0f);
            fillLight = fillGo.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.7f;
            fillLight.color = new Color(0.6f, 0.8f, 1f);
        }
    }

    private void EnsurePedestal()
    {
        if (stagePedestal == null)
        {
            var pedGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedGo.name = "StagePedestal";
            pedGo.transform.SetParent(transform, false);
            pedGo.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            pedGo.transform.localScale = new Vector3(1.6f, 0.05f, 1.6f);

            var col = pedGo.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = pedGo.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.12f, 0.16f, 0.22f);
                mat.SetFloat("_Smoothness", 0.8f);
                mr.material = mat;
            }
            stagePedestal = pedGo.transform;
        }
    }

    public void SetHair(int index)
    {
        desiredHairIndex = index;
        EnsureCustomModelReference();
        customModel?.SetHair(index);
    }

    public void SetEye(int index)
    {
        desiredEyeIndex = index;
        EnsureCustomModelReference();
        customModel?.SetEye(index);
    }

    public void SetMouth(int index)
    {
        desiredMouthIndex = index;
        EnsureCustomModelReference();
        customModel?.SetMouth(index);
    }

    public void ApplyCustomization(int hairIndex, int eyeIndex, int mouthIndex)
    {
        desiredHairIndex = hairIndex;
        desiredEyeIndex = eyeIndex;
        desiredMouthIndex = mouthIndex;
        EnsureCustomModelReference();
        customModel?.ApplyCustomization(hairIndex, eyeIndex, mouthIndex);
    }

    /// <summary>
    /// 캐릭터 모델이 Addressable 로드로 늦게 생성된 경우를 대비해, 매 호출 시 참조를 다시 확인한다.
    /// </summary>
    private void EnsureCustomModelReference()
    {
        if (customModel == null && characterModelInstance != null)
        {
            customModel = characterModelInstance.GetComponent<CharacterCustomModel>();
        }
    }

    /// <summary>
    /// CharacterCustomModel에 현재 적용된 외형(헤어/눈/입)을 세이브 데이터에 반영해 저장한다.
    /// 이미 생성된 캐릭터가 있을 때만 성공하며(최초 캐릭터 생성은 LobbySceneController가 별도로 처리),
    /// 생성 이후 외형을 다시 바꾸는 기능(예: 옷장/재커스터마이징 화면)에서 재사용하기 위한 진입점이다.
    /// </summary>
    public void SaveCurrentCustomization(Action<bool> _onComplete = null)
    {
        EnsureCustomModelReference();

        if (customModel == null)
        {
            DebugLogManager.GenerateErrorMessage<CharacterPreviewStage>("customModel이 없어 외형을 저장할 수 없습니다.");
            _onComplete?.Invoke(false);
            return;
        }

        if (SaveDataManager.Instance == null)
        {
            _onComplete?.Invoke(false);
            return;
        }

        SaveDataManager.Instance.UpdateCharacterCustomization(
            customModel.CurrentHairIndex,
            customModel.CurrentEyeIndex,
            customModel.CurrentMouthIndex,
            _onComplete);
    }

    /// <summary>
    /// 수동 회전 제어 (드래그 시 호출)
    /// </summary>
    public void AddRotation(float deltaX)
    {
        targetYaw -= deltaX * rotationSensitivity;
    }

    public void RotateLeft(float angle = 45f)
    {
        targetYaw += angle;
    }

    public void RotateRight(float angle = 45f)
    {
        targetYaw -= angle;
    }

    public void ResetRotation()
    {
        targetYaw = 180f;
        currentYaw = 180f;
        if (characterRoot != null)
        {
            characterRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
    }

    public void SetAutoRotate(bool enabled)
    {
        autoRotate = enabled;
    }

    private void CleanupRenderTexture()
    {
        if (previewCamera != null && previewCamera.targetTexture == previewRenderTexture)
        {
            previewCamera.targetTexture = null;
        }

        if (previewRenderTexture != null)
        {
            previewRenderTexture.Release();
            Destroy(previewRenderTexture);
            previewRenderTexture = null;
        }
    }
    #endregion
}
