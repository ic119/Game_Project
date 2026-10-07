using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using Incheol.View.UI;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public partial class GameSceneManager : MonoBehaviour
    {
        #region Variable
        private const string gameSceneTag = "GameScene";

        private UI_GameSceneView gameSceneView;
        private PlayerCharacterModel spawnedPlayerModel;
        private MiniMapController miniMapController;
        private UI_ChatView chatView;
        private UI_MonsterTargetView monsterTargetView;
        private UI_DropItemPopupView dropItemPopupView;
        private UI_PlayerRespawnPopupView respawnPopupView;
        private UI_MapViewPopupView mapViewPopupView;
        private WorldMapController worldMapController;

        /// <summary>
        /// 사망 후 자동 부활까지 걸리는 시간(초). 서버 CombatTuning.ReviveDelaySeconds와 같아야 한다(CombatTimings가 단일 출처, 서버 테스트가 일치를 검사한다) - 서버는 남은 시간을 보내지 않으므로
        /// 부활 팝업(UI_PlayerRespawnPopupView)의 카운트다운에 이 값을 쓴다. 실제 부활은 서버의 Game_PlayerRevived를 받을 때 일어난다.
        /// </summary>
        private const float ReviveDelaySeconds = CombatTimings.ReviveDelaySeconds;
        private float lastMonsterTargetedTime;
        private const float MonsterTargetLostTimeoutSeconds = 8f;

        private GameObject inventoryInstance;
        private UI_InventoryView inventoryView;

        /// <summary>
        /// 로컬 플레이어가 처치 보상(Game_LootBroadcast)으로 받은 아이템의 런타임 누적 상태.
        /// itemId별 수량과 장착 슬롯(equipSlot)을 들고 있으며, 서버(CharacterItem)와 1:1로 대응한다.
        /// </summary>
        private readonly List<InventoryItemStack> localInventoryItems = new List<InventoryItemStack>();

        /// <summary>
        /// 현재 로드되어 있는 맵 프리팹 인스턴스와 그 Addressable 키.
        /// MapPortalController(PortalTeleportType.MapSwap)가 SwapMap을 호출할 때 이전 맵을 정리하는 데 쓴다.
        /// </summary>
        private GameObject currentMapInstance;
        private string currentMapId = nameof(AddressableAssetKey.Floor001);

        /// <summary>
        /// SwapMapAsync가 진행 중인 동안 true. 앞으로 맵이 늘어나 포털을 연달아 통과하거나 같은 프레임에
        /// 중복 호출되는 상황이 잦아질 것을 대비해, 이전 전환이 끝나기 전 새 요청을 무시한다.
        /// </summary>
        private bool isSwappingMap;

        /// <summary>
        /// 물약 사용 요청(Game_UseItemRequest)을 보내고 결과(Game_UseItemResult)를 기다리는 중이면 true.
        /// 응답 전 연타로 같은 요청이 여러 번 나가지 않게 막는다.
        /// </summary>
        private bool isUseItemPending;

        /// <summary>
        /// 물약 재사용 대기시간이 끝나는 시각(Time.unscaledTime 기준). 서버가 Game_UseItemResult로 알려준 남은 시간으로 갱신한다 -
        /// 실제 판정은 서버가 하고(모든 물약이 하나의 대기시간을 공유), 이 값은 어차피 거부될 요청을 미리 거르고 남은 시간을
        /// 보여주는 데 쓴다.
        /// </summary>
        private float potionReadyAtTime;

        /// <summary>
        /// 다음 물약을 쓸 수 있기까지 남은 시간(초). 대기 중이 아니면 0.
        /// </summary>
        public float PotionCooldownRemainingSeconds => Mathf.Max(0f, potionReadyAtTime - Time.unscaledTime);

        /// <summary>
        /// 로컬 플레이어 인스턴스. SpawnPlayerCharacter가 이 GameSceneManager(transform) 밑에 생성하고
        /// RespawnPoint의 위치/회전값만 가져다 쓰므로, 맵 프리팹(및 그 안의 RespawnPoint)이 파괴돼도
        /// 함께 파괴되지 않는다.
        /// </summary>
        private GameObject localPlayerInstance;

        /// <summary>
        /// 로컬 플레이어의 PlayerAttackController. MonsterTargeted 구독 해지(OnDestroy)를 위해 들고 있는다.
        /// </summary>
        private PlayerAttackController localPlayerAttackController;

        /// <summary>
        /// 인벤토리 UI(inventoryInstance)의 현재 활성화 여부를 들고 있는 상태값.
        /// I키 토글 시 gameObject.activeSelf를 직접 확인하는 대신 이 값을 기준(source of truth)으로 판단한다.
        /// </summary>
        private bool isInventoryActive = false;

        /// <summary>
        /// logoutButton 연타로 로그아웃 요청이 중복 전송되는 것을 막는 가드.
        /// </summary>
        private bool isLoggingOut = false;

        // 로비에서 시작 버튼으로 들어오면 SceneLoadManager가 프리로드를 100%까지 채운 뒤 GameScene으로 전환하고, 로딩바는 100%인 채로
        // 넘겨준다. 맵/UI/플레이어 생성과 서버 입장은 이 씬에서 이어지므로 여기서는 제목만 바꿔 안내하고, 입장이 확인되면 숨긴다.

        // 입장 응답이 오지 않는 등 예상 밖의 이유로 로딩바가 화면을 영원히 가리지 않도록 하는 안전장치.
        private const float InitialLoadingTimeoutSeconds = 30f;

        /// <summary>이 씬이 이어받은 최초 로딩바를 아직 숨기지 않았는지.</summary>
        private bool isInitialLoadingBarActive;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            // 맵/플레이어/몬스터가 전부 이 transform의 자식으로 생성된다. 몬스터 스폰 좌표는 에디터에서
            // 맵 프리팹만 고립시켜 내보낸 값(사실상 로컬 좌표, MonsterSpawnPointExporter 참고)이라
            // 이 오브젝트가 원점이 아니면 서버가 아는 몬스터 좌표와 플레이어의 실제 월드 좌표계가
            // 어긋나 버린다(스폰 위치는 물론 인식/추적 AI 판정도 깨진다). Scene 뷰에서 실수로 옮겨질
            // 수 있으니 부팅 시점에 바로 경고한다.
            if (transform.position != Vector3.zero)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>(
                    $"GameSceneManager가 원점이 아닙니다({transform.position}). 맵/몬스터 좌표계가 어긋날 수 있으니 Transform을 (0,0,0)으로 되돌리세요.");
            }
        }

        private void Start()
        {
            BeginInitialLoading();
            LoadAndInstantiateGameSceneAssets();
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnChatReceived += HandleChatReceived;
                GameServerConnectManager.Instance.OnDamageReceived += HandleDamageReceived;
                GameServerConnectManager.Instance.OnMonsterAttacked += HandleMonsterAttackReceived;
                GameServerConnectManager.Instance.OnMonsterAttackDodged += HandleMonsterAttackDodged;
                GameServerConnectManager.Instance.OnMapChangeAcked += HandleMapChangeAcked;
                GameServerConnectManager.Instance.OnMapChangeRejected += HandleMapChangeRejected;
                GameServerConnectManager.Instance.OnExpGained += HandleExpGained;
                GameServerConnectManager.Instance.OnLootReceived += HandleLootReceived;
                GameServerConnectManager.Instance.OnPlayerHpChanged += HandlePlayerHpChanged;
                GameServerConnectManager.Instance.OnPlayerRevived += HandlePlayerRevived;
                GameServerConnectManager.Instance.OnPlayerMpChanged += HandlePlayerMpChanged;
                GameServerConnectManager.Instance.OnUseItemResult += HandleUseItemResult;
                GameServerConnectManager.Instance.OnPositionCorrected += HandlePositionCorrected;
                GameServerConnectManager.Instance.OnServerError += HandleGameServerError;
                GameServerConnectManager.Instance.OnKicked += HandleSessionKicked;
                GameServerConnectManager.Instance.OnDisconnected += HandleGameServerDisconnected;
                GameServerConnectManager.Instance.OnEntered += HandleGameServerEntered;
                GameServerConnectManager.Instance.OnReconnecting += HandleGameServerReconnecting;
                GameServerConnectManager.Instance.OnReconnected += HandleGameServerReconnected;
            }

            // ItemDatabaseSO는 Addressables로 비동기 로드되므로, 씬 진입 직후 인벤토리를 처음 열면 로드가
            // 아직 안 끝나 아이템 아이콘/이름이 비어 보일 수 있다. 로드가 끝나는 즉시 한 번 더 갱신해
            // 이미 열려 있었던(또는 그 사이 열렸다 닫힌) 인벤토리도 뒤늦게 정상 표시되게 한다.
            if (ItemDatabaseManager.Instance != null)
            {
                ItemDatabaseManager.Instance.OnDatabaseLoaded += HandleItemDatabaseLoaded;
            }
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnChatReceived -= HandleChatReceived;
                GameServerConnectManager.Instance.OnDamageReceived -= HandleDamageReceived;
                GameServerConnectManager.Instance.OnMonsterAttacked -= HandleMonsterAttackReceived;
                GameServerConnectManager.Instance.OnMonsterAttackDodged -= HandleMonsterAttackDodged;
                GameServerConnectManager.Instance.OnMapChangeAcked -= HandleMapChangeAcked;
                GameServerConnectManager.Instance.OnMapChangeRejected -= HandleMapChangeRejected;
                GameServerConnectManager.Instance.OnExpGained -= HandleExpGained;
                GameServerConnectManager.Instance.OnLootReceived -= HandleLootReceived;
                GameServerConnectManager.Instance.OnPlayerHpChanged -= HandlePlayerHpChanged;
                GameServerConnectManager.Instance.OnPlayerRevived -= HandlePlayerRevived;
                GameServerConnectManager.Instance.OnPlayerMpChanged -= HandlePlayerMpChanged;
                GameServerConnectManager.Instance.OnUseItemResult -= HandleUseItemResult;
                GameServerConnectManager.Instance.OnPositionCorrected -= HandlePositionCorrected;
                GameServerConnectManager.Instance.OnServerError -= HandleGameServerError;
                GameServerConnectManager.Instance.OnKicked -= HandleSessionKicked;
                GameServerConnectManager.Instance.OnDisconnected -= HandleGameServerDisconnected;
                GameServerConnectManager.Instance.OnEntered -= HandleGameServerEntered;
                GameServerConnectManager.Instance.OnReconnecting -= HandleGameServerReconnecting;
                GameServerConnectManager.Instance.OnReconnected -= HandleGameServerReconnected;
            }

            if (ItemDatabaseManager.Instance != null)
            {
                ItemDatabaseManager.Instance.OnDatabaseLoaded -= HandleItemDatabaseLoaded;
            }
        }

        private void Update()
        {
            // 채팅 입력 중에 "i"를 치면 인벤토리가 열리고 닫히지 않도록 단축키를 읽지 않는다(InputBlocker).
            if (!InputBlocker.IsBlocked && Input.GetKeyDown(KeyCode.I))
            {
                ToggleInventory();
            }

            if (!InputBlocker.IsBlocked && Input.GetKeyDown(KeyCode.M))
            {
                if (mapViewPopupView != null)
                {
                    if (mapViewPopupView.IsOpen)
                    {
                        mapViewPopupView.Close();
                    }
                    else
                    {
                        mapViewPopupView.Open();
                    }
                }
            }

            if (monsterTargetView != null && monsterTargetView.HasTarget &&
                Time.time - lastMonsterTargetedTime > MonsterTargetLostTimeoutSeconds)
            {
                monsterTargetView.ClearTarget();
            }
        }

        private void OnDestroy()
        {
            // 맵 이동 도중에 이 매니저가 파괴되면(씬 전환 등) SwapMapAsync의 finally가 실행되지 않을 수 있어, 입력 잠금이 남지 않게 푼다.
            InputBlocker.SetBlocked(this, false);

            // 최초 로딩 도중에 씬이 사라지면(예: 로딩 중 세션 만료로 로그인 화면 전환) 이어받은 로딩바가 남지 않게 숨긴다.
            HideInitialLoadingBar();

            if (gameSceneView != null)
            {

                gameSceneView.LogoutButtonClicked -= HandleLogoutButtonClicked;
            }

            if (chatView != null)
            {
                chatView.MessageSubmitted -= HandleChatMessageSubmitted;
            }

            if (mapViewPopupView != null)
            {
                mapViewPopupView.Opened -= HandleMapViewOpened;
                mapViewPopupView.Closed -= HandleMapViewClosed;
            }

            if (localPlayerAttackController != null)
            {
                localPlayerAttackController.MonsterTargeted -= HandleMonsterTargeted;
            }

            if (inventoryView != null)
            {
                inventoryView.OnUseItemRequested -= HandleInventoryUseRequested;
                inventoryView.OnDropItemRequested -= HandleInventoryDropRequested;

            }

            // GameScene을 벗어나면(씬 전환) GameServer 접속을 종료한다 - PersistAcrossScenes로 유지되는
            // GameServerConnectManager는 씬 전환만으로는 파괴되지 않으므로 명시적으로 끊어줘야 한다.
            GameServerConnectManager.Instance?.Disconnect();
        }

        #endregion

        #region Method
        /// <summary>
        /// 이전 씬(로비)에서 이어받은 로딩바가 떠 있으면 이 씬이 숨길 책임을 진다. GameScene을 에디터에서 바로 실행하는 등
        /// 로딩바가 없으면 아무 일도 하지 않는다. 입장 응답(OnEntered)이 끝내 오지 않는 경우를 대비해 타임아웃을 건다.
        /// </summary>
        private void BeginInitialLoading()
        {
            LoadingBarViewState state = GameManager.Instance != null && GameManager.Instance.LoadingBarView != null
                ? (GameManager.Instance.LoadingBarView.gameObject.activeInHierarchy ? LoadingBarViewState.Visible : LoadingBarViewState.Hidden)
                : LoadingBarViewState.Missing;

            isInitialLoadingBarActive = state == LoadingBarViewState.Visible;

            if (isInitialLoadingBarActive)
            {
                _ = InitialLoadingTimeoutAsync();
            }
        }

        private enum LoadingBarViewState
        {
            Missing,
            Hidden,
            Visible
        }

        private async Awaitable InitialLoadingTimeoutAsync()
        {
            await Awaitable.WaitForSecondsAsync(InitialLoadingTimeoutSeconds);

            if (this == null || !isInitialLoadingBarActive)
            {
                return;
            }

            DebugLogManager.GenerateErrorMessage<GameSceneManager>($"게임 서버 입장 확인이 {InitialLoadingTimeoutSeconds}초 안에 오지 않아 로딩바를 강제로 숨깁니다.");
            HideInitialLoadingBar();
        }

        /// <summary>최초 로딩 중일 때만 로딩바의 제목을 바꾼다. 진행률은 SceneLoadManager가 이미 100%로 채운 상태 그대로 둔다.</summary>
        private void ReportInitialLoading(string _title)
        {
            if (!isInitialLoadingBarActive)
            {
                return;
            }

            GameManager.Instance?.LoadingBarView?.UpdateTitle(_title);
        }

        /// <summary>서버 입장이 확인되면 100%를 잠깐 보여준 뒤 로딩바를 숨긴다.</summary>
        private async Awaitable CompleteInitialLoadingAsync()
        {
            if (!isInitialLoadingBarActive)
            {
                return;
            }

            ReportInitialLoading("입장 완료");
            await Awaitable.WaitForSecondsAsync(0.2f);

            if (this == null)
            {
                return;
            }

            HideInitialLoadingBar();
        }

        /// <summary>이어받은 최초 로딩바를 숨긴다. 여러 번 불러도 한 번만 동작한다(맵 이동/재접속 로딩바와 섞이지 않는다).</summary>
        private void HideInitialLoadingBar()
        {
            if (!isInitialLoadingBarActive)
            {
                return;
            }

            isInitialLoadingBarActive = false;

            // LoadingBarView는 풀에서 재사용되는 인스턴스라 다음 사용처에 이 문구가 남지 않게 비운다.
            GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);
            GameManager.Instance?.HideLoadingBar();
        }

        /// <summary>
        /// AddressableAssetModelSO에서 tags가 "GameScene"인 항목의 preloadAddressableKeys(예: UI_GameScene)를 로드하여
        /// 이 GameSceneManager(this.transform)의 자식으로 직접 생성한다.
        /// GameScene 전용 에셋은 씬이 언로드될 때 함께 파괴되어야 하므로, ObjectPoolManager(PersistAcrossScenes) 기반인
        /// GameManager.LoadAndInstantiateByTag와는 별도로 여기서 직접 로드/인스턴스화한다.
        /// </summary>
        private async void LoadAndInstantiateGameSceneAssets()
        {
            if (GameManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("GameManager.Instance가 null입니다.");
                return;
            }

            List<AddressableAssetKey> keys = await GameManager.Instance.LoadAddressableKeysByTagAsync(gameSceneTag);

            if (keys == null || this == null)
            {
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            ReportInitialLoading("맵과 UI를 구성하는 중...");

            foreach (AddressableAssetKey key in keys)
            {
                if (key == AddressableAssetKey.None)
                {
                    continue;
                }

                string keyString = key.ToString();

                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(keyString, prefab =>
                {
                    if (this == null)
                    {
                        return;
                    }

                    if (prefab == null)
                    {
                        DebugLogManager.GenerateErrorMessage<GameSceneManager>($"GameScene Addressable 로드 실패 Key : {keyString}");
                        return;
                    }

                    GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);

                    // Floor001은 단순 프리로드 대상이 아니라 플레이어가 실제로 배치될 맵이므로,
                    // 생성 직후 맵 안의 RespawnPoint를 찾아 그 자리에 선택된 캐릭터를 스폰한다.
                    if (key == AddressableAssetKey.Floor001)
                    {
                        currentMapInstance = instance;
                        currentMapId = keyString;
                        SpawnPlayerAtRespawnPoint(instance);
                    }

                    // UI_GameScene과 플레이어(Floor001 하위에서 비동기로 스폰됨)는 로드 완료 순서가 보장되지 않으므로,
                    // 둘 다 준비된 시점에 TryBindPlayerInfo가 캐릭터 정보를 UI에 반영한다.
                    if (key == AddressableAssetKey.UI_GameScene)
                    {
                        instance.TryGetComponent(out gameSceneView);
                        instance.TryGetComponent(out chatView);
                        instance.TryGetComponent(out monsterTargetView);
                        instance.TryGetComponent(out dropItemPopupView);
                        instance.TryGetComponent(out respawnPopupView);
                        instance.TryGetComponent(out mapViewPopupView);

                        if (gameSceneView != null)
                        {
                            gameSceneView.LogoutButtonClicked += HandleLogoutButtonClicked;
                        }

                        if (chatView != null)
                        {
                            chatView.MessageSubmitted += HandleChatMessageSubmitted;
                        }

                        TryBindPlayerInfo();
                    }
                });
            }
        }

        /// <summary>
        /// Floor001 맵 인스턴스 하위에서 "RespawnPoint" 이름의 Transform을 찾아 그 위치에 플레이어 캐릭터를 스폰한다.
        /// </summary>
        private void SpawnPlayerAtRespawnPoint(GameObject _mapInstance)
        {
            if (_mapInstance == null)
            {
                return;
            }

            Transform respawnPoint = FindChildRecursive(_mapInstance.transform, "RespawnPoint");

            if (respawnPoint == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("Floor001 맵에서 RespawnPoint를 찾을 수 없습니다.");
                return;
            }

            SpawnPlayerCharacter(respawnPoint);
        }

        private static Transform FindChildRecursive(Transform _root, string _name)
        {
            if (_root.name == _name)
            {
                return _root;
            }

            foreach (Transform child in _root)
            {
                Transform found = FindChildRecursive(child, _name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }


        /// <summary>
        /// BasicCharacter를 Addressable로 로드해 이 GameSceneManager(transform) 밑에 생성하고,
        /// _respawnPoint의 위치/회전값만 가져다 배치한다(그 자식으로 만들지는 않는다 - 맵 프리팹 하위에
        /// 있는 RespawnPoint에 종속되면 맵이 파괴/교체될 때 플레이어도 함께 파괴될 위험이 있다).
        /// SaveDataManager에 저장된 선택 캐릭터의 외형(헤어/눈/입)을 적용한다.
        /// </summary>
        private void SpawnPlayerCharacter(Transform _respawnPoint)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.BasicCharacter.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"플레이어 캐릭터 로드 실패 Key : {AddressableAssetKey.BasicCharacter}");
                    return;
                }

                GameObject playerInstance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                playerInstance.transform.SetPositionAndRotation(_respawnPoint.position, _respawnPoint.rotation);
                localPlayerInstance = playerInstance;

                ApplySelectedCharacterCustomization(playerInstance);
                AssignPlayerToFollowCamera(playerInstance.transform);

                // RequireComponent로 Rigidbody/CapsuleCollider가 함께 추가되어, 스폰 직후 중력을 받아 지면에 착지하고
                // WASD로 카메라 기준 이동할 수 있게 된다(이동 방향으로 몸이 자동으로 돈다).
                playerInstance.AddComponent<PlayerMoveController>();

                // 일정 주기로 자신의 위치/회전을 GameServer(Game_MoveRequest)로 전송한다.
                playerInstance.AddComponent<PlayerNetworkSender>();

                // Space 입력으로 전방의 원격 플레이어를 공격(Game_AttackRequest)한다.
                localPlayerAttackController = playerInstance.AddComponent<PlayerAttackController>();
                localPlayerAttackController.MonsterTargeted += HandleMonsterTargeted;

                // 숫자 키 1~4로 액티브 스킬을 쓴다(PlayerAttackController/PlayerMoveController를 찾아 쓰므로 둘보다 뒤에 붙인다).
                playerInstance.AddComponent<PlayerSkillController>();

                ReportInitialLoading("캐릭터 정보를 불러오는 중...");
            });
        }

        /// <summary>
        /// 씬의 CinemachineCamera(CM_PlayerFollowCamera)가 방금 스폰된 플레이어를 추적하도록 Follow 타깃을 연결한다.
        /// Floor001/RespawnPoint 등 씬 구성 에셋과 달리 카메라는 GameScene.unity에 이미 배치되어 있으므로 여기서는 찾아서 연결만 한다.
        /// </summary>
        private void AssignPlayerToFollowCamera(Transform _playerTransform)
        {
            CinemachineCamera followCamera = FindAnyObjectByType<CinemachineCamera>();

            if (followCamera == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("씬에서 CinemachineCamera를 찾을 수 없습니다.");
                return;
            }

            // 이동/전투 카메라는 수평 45도로 고정된 월드 기준 CinemachineFollow + RotationComposer라 캐릭터가 돌아도 화면이 돌지 않는다.
            // CustomLookAtTarget이 꺼져 있어 LookAt은 Follow 대상을 그대로 쓰므로 따로 지정하지 않는다.
            followCamera.Follow = _playerTransform;
        }

        /// <summary>
        /// SaveDataManager.SelectedCharacterId를 서버에서 다시 조회하여(헤어/눈/입 포함),
        /// 방금 생성한 플레이어 인스턴스의 CharacterCustomModel에 적용한다.
        /// </summary>
        private void ApplySelectedCharacterCustomization(GameObject _playerInstance)
        {
            if (SaveDataManager.Instance == null || !SaveDataManager.Instance.SelectedCharacterId.HasValue)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("선택된 캐릭터가 없어 외형을 적용할 수 없습니다.");
                return;
            }

            if (!_playerInstance.TryGetComponent(out CharacterCustomModel customModel))
            {
                return;
            }

            SaveDataManager.Instance.FetchCharacterDetailAsync(SaveDataManager.Instance.SelectedCharacterId.Value, userSaveData =>
            {
                if (_playerInstance == null || userSaveData == null)
                {
                    return;
                }

                customModel.ApplyCustomization(userSaveData.hairIndex, userSaveData.eyeIndex, userSaveData.mouthIndex);

                if (_playerInstance.TryGetComponent(out PlayerCharacterModel playerModel))
                {
                    // 닉네임/레벨/체력/공격력·방어력을 세이브 데이터로 초기화한다(경험치는 서버 미지원으로 0에서 시작).
                    playerModel.ApplyUserSaveData(userSaveData);

                    spawnedPlayerModel = playerModel;
                    TryBindPlayerInfo();
                }

                // 이전 세션에서 처치 보상으로 쌓인 아이템(서버 CharacterItems)을 복원한다. 골드는
                // ApplyUserSaveData가 이미 spawnedPlayerModel.Gold로 반영했으므로 여기서는 아이템만 채운다.
                localInventoryItems.Clear();
                localInventoryItems.AddRange(userSaveData.items);

                // 복원한 아이템 중 equipSlot이 설정된(이전 세션에 장착해뒀던) 것들을 캐릭터 시각에 반영하고,
                // 장비 스탯 보너스를 계산해둔다 - 이 직후 ConnectToGameServer가 만드는 GamePlayerInfo가
                // spawnedPlayerModel.AttackPower/Defense를 그대로 읽으므로, Enter 시점부터 이미 보너스가 실려 간다.
                ApplyEquippedVisuals();
                RecalculateEquipmentStats();

                // 캐릭터 생성(외형/스탯 적용)이 성공적으로 끝난 시점에 인벤토리 UI를 비활성 상태로 미리 만들어둔다.
                SpawnInventoryUI();

                ConnectToGameServer(_playerInstance, userSaveData);
            });
        }

        /// <summary>
        /// 외형/닉네임이 확정된 시점(ApplySelectedCharacterCustomization 콜백)에 GameServer(TCP)에 접속해 자신의
        /// 캐릭터를 입장시키고(Game_EnterRequest), 다른 접속자의 입장/퇴장/이동을 처리할 RemotePlayerManager를 활성화한다.
        /// </summary>
        private void ConnectToGameServer(GameObject _playerInstance, UserSaveData _userSaveData)
        {
            if (GameServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("GameServerConnectManager.Instance가 null입니다.");
                return;
            }

            Vector3 position = _playerInstance.transform.position;
            var localInfo = new GamePlayerInfo
            {
                PlayerId = _userSaveData.characterId,
                Nickname = _userSaveData.nickname,
                MapId = currentMapId,
                HairIndex = _userSaveData.hairIndex,
                EyeIndex = _userSaveData.eyeIndex,
                MouthIndex = _userSaveData.mouthIndex,
                X = position.x,
                Y = position.y,
                Z = position.z,
                RotationY = _playerInstance.transform.eulerAngles.y,
                // ApplySelectedCharacterCustomization이 이미 ApplyUserSaveData를 호출해 spawnedPlayerModel의
                // 체력/공격력/방어력이 채워진 뒤라 여기서 바로 읽어 보낼 수 있다.
                MaxHp = spawnedPlayerModel != null ? spawnedPlayerModel.MaxHp : 0,
                CurrentHp = spawnedPlayerModel != null ? spawnedPlayerModel.CurrentHp : 0,
                MaxMp = spawnedPlayerModel != null ? spawnedPlayerModel.MaxMp : 0,
                CurrentMp = spawnedPlayerModel != null ? spawnedPlayerModel.CurrentMp : 0,
                AttackPower = spawnedPlayerModel != null ? spawnedPlayerModel.AttackPower : 0,
                Defense = spawnedPlayerModel != null ? spawnedPlayerModel.Defense : 0,
                Level = spawnedPlayerModel != null ? spawnedPlayerModel.Level : 1,
                Exp = spawnedPlayerModel != null ? spawnedPlayerModel.CurrentExp : 0
            };

            ReportInitialLoading("게임 서버에 접속하는 중...");

            GameServerConnectManager.Instance.ConnectAndEnter(localInfo);

            // 다른 접속자의 입장/퇴장/이동, 몬스터 스폰/피격/사망 이벤트 구독을 시작한다
            // (최초 접근 시 SingletonObject가 자동 생성된다).
            _ = RemotePlayerManager.Instance;
            _ = RemoteMonsterManager.Instance;
            _ = DamageTextManager.Instance;
            _ = RemoteChestManager.Instance;
            _ = RemoteGateManager.Instance;
        }

        /// <summary>
        /// UI_GameScene 인스턴스화와 플레이어 스폰(둘 다 비동기)이 모두 끝난 시점에만
        /// UI_GameSceneView.BindPlayer를 호출해 캐릭터 정보를 화면에 반영한다.
        /// </summary>
        private void TryBindPlayerInfo()
        {
            if (gameSceneView != null && spawnedPlayerModel != null)
            {
                gameSceneView.BindPlayer(spawnedPlayerModel);
                SetupMiniMap();
                SetupWorldMap();
            }
        }

        /// <summary>
        /// UI_GameScene과 로컬 플레이어가 모두 준비된 시점(TryBindPlayerInfo)에 한 번만 MiniMapController를
        /// 생성해 초기화한다. gameSceneView.miniMapView가 인스펙터에 연결돼 있지 않으면 조용히 건너뛴다.
        /// </summary>
        private void SetupMiniMap()
        {
            if (miniMapController != null || localPlayerInstance == null)
            {
                return;
            }

            if (gameSceneView.MiniMapView == null)
            {
                return;
            }

            GameObject miniMapObject = new GameObject(nameof(MiniMapController));
            miniMapObject.transform.SetParent(transform, false);

            miniMapController = miniMapObject.AddComponent<MiniMapController>();
            miniMapController.Initialize(gameSceneView.MiniMapView, gameSceneView.PlayerMiniMapIcon, localPlayerInstance.transform);
        }

        /// <summary>
        /// 맵 팝업(M키)에 현재 맵 전체를 보여주는 WorldMapController를 한 번만 생성/초기화한다.
        /// 팝업이 열릴 때 현재 맵(currentMapInstance) 기준으로 영역을 다시 맞추고, 닫히면 렌더링을 멈춘다.
        /// </summary>
        private void SetupWorldMap()
        {
            if (worldMapController != null || localPlayerInstance == null || mapViewPopupView == null || mapViewPopupView.MapView == null)
            {
                return;
            }

            GameObject worldMapObject = new GameObject(nameof(WorldMapController));
            worldMapObject.transform.SetParent(transform, false);

            worldMapController = worldMapObject.AddComponent<WorldMapController>();
            worldMapController.Initialize(mapViewPopupView.MapView, gameSceneView != null ? gameSceneView.PlayerMiniMapIcon : null, localPlayerInstance.transform);

            mapViewPopupView.Opened += HandleMapViewOpened;
            mapViewPopupView.Closed += HandleMapViewClosed;
        }

        private void HandleMapViewOpened()
        {
            worldMapController?.Show(currentMapInstance);
        }

        private void HandleMapViewClosed()
        {
            worldMapController?.Hide();
        }

        /// <summary>
        /// UI_Inventory를 Addressable로 로드해 이 GameSceneManager(this.transform)의 자식으로 생성하되,
        /// 처음에는 비활성 상태로 만들어둔다. 이후 I키 입력(Update → ToggleInventory)으로 켜고 끈다.
        /// </summary>
        private void SpawnInventoryUI()
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.UI_Inventory.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"인벤토리 UI 로드 실패 Key : {AddressableAssetKey.UI_Inventory}");
                    return;
                }

                inventoryInstance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                inventoryInstance.SetActive(false);
                isInventoryActive = false;

                if (inventoryInstance.TryGetComponent(out inventoryView))
                {
                    inventoryView.OnUseItemRequested += HandleInventoryUseRequested;
                    inventoryView.OnDropItemRequested += HandleInventoryDropRequested;

                }

                // UI_InventoryView.Awake()가 임시 플레이스홀더 골드값으로 초기화해두므로, 생성 직후 실제
                // 세이브 데이터 값으로 즉시 덮어쓴다(그 사이 처치 보상을 먼저 받는 레이스는 없다 - 인벤토리는
                // 캐릭터 커스터마이징이 끝난 뒤에야 생성되고, GameServer 접속/전투는 그다음에 시작된다).
                RefreshInventoryDisplay();
            });
        }






        #endregion
    }
}
