using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Presenter.Scene
{
    public class GameSceneManager : MonoBehaviour
    {
        #region Variable
        private const string gameSceneTag = "GameScene";

        private UI_GameSceneView gameSceneView;
        private PlayerCharacterModel spawnedPlayerModel;
        private MiniMapController miniMapController;
        private UI_ChatView chatView;
        private UI_MonsterTargetView monsterTargetView;
        private UI_DropItemPopupView dropItemPopupView;
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
            LoadAndInstantiateGameSceneAssets();
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnChatReceived += HandleChatReceived;
                GameServerConnectManager.Instance.OnDamageReceived += HandleDamageReceived;
                GameServerConnectManager.Instance.OnMonsterAttacked += HandleMonsterAttackReceived;
                GameServerConnectManager.Instance.OnExpGained += HandleExpGained;
                GameServerConnectManager.Instance.OnLootReceived += HandleLootReceived;
                GameServerConnectManager.Instance.OnServerError += HandleGameServerError;
                GameServerConnectManager.Instance.OnDisconnected += HandleGameServerDisconnected;
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
                GameServerConnectManager.Instance.OnExpGained -= HandleExpGained;
                GameServerConnectManager.Instance.OnLootReceived -= HandleLootReceived;
                GameServerConnectManager.Instance.OnServerError -= HandleGameServerError;
                GameServerConnectManager.Instance.OnDisconnected -= HandleGameServerDisconnected;
            }

            if (ItemDatabaseManager.Instance != null)
            {
                ItemDatabaseManager.Instance.OnDatabaseLoaded -= HandleItemDatabaseLoaded;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleInventory();
            }

            if (monsterTargetView != null && monsterTargetView.HasTarget &&
                Time.time - lastMonsterTargetedTime > MonsterTargetLostTimeoutSeconds)
            {
                monsterTargetView.ClearTarget();
            }
        }

        private void OnDestroy()
        {
            if (gameSceneView != null)
            {

                gameSceneView.LogoutButtonClicked -= HandleLogoutButtonClicked;
            }

            if (chatView != null)
            {
                chatView.MessageSubmitted -= HandleChatMessageSubmitted;
            }

            if (localPlayerAttackController != null)
            {
                localPlayerAttackController.MonsterTargeted -= HandleMonsterTargeted;
            }

            if (inventoryView != null)
            {
                inventoryView.OnUseItemRequested -= HandleInventoryUseRequested;
            }

            // GameScene을 벗어나면(씬 전환) GameServer 접속을 종료한다 - PersistAcrossScenes로 유지되는
            // GameServerConnectManager는 씬 전환만으로는 파괴되지 않으므로 명시적으로 끊어줘야 한다.
            GameServerConnectManager.Instance?.Disconnect();
        }

        #endregion

        #region Method
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
        /// MapPortalController(PortalTeleportType.MapSwap)가 호출한다. Scene을 전환하지 않고 현재 맵 프리팹만
        /// 제거한 뒤 새 맵을 로드해서, 그 안의 _entryPointName Transform으로 로컬 플레이어를 옮긴다.
        /// GameSceneManager/UI_GameScene/GameServerConnectManager 접속은 그대로 유지된다.
        /// 맵/엔트리포인트 이름은 전부 인자로 받으므로, 새 맵을 추가할 때 이 메서드 자체는 건드릴 필요 없이
        /// AddressableAssetKey에 항목을 추가하고 MapPortalController에서 그 키를 가리키기만 하면 된다.
        /// </summary>
        public void SwapMap(AddressableAssetKey _newMapKey, string _entryPointName = "RespawnPoint")
        {
            _ = SwapMapAsync(_newMapKey, _entryPointName);
        }

        /// <summary>
        /// SwapMap의 실제 구현. 진행되는 동안(현재 맵 제거 -> 새 맵 생성 -> 플레이어 재배치) GameManager의
        /// UI_LoadingBarView를 띄워 빈 화면이 보이지 않게 가리고, 끝나면 100%로 채운 뒤 다시 숨긴다.
        /// try/finally로 감싸 어떤 경로로 리턴하든(성공/실패/조기 취소) isSwappingMap 해제와 로딩바 숨김이
        /// 항상 실행되도록 보장한다 - 맵이 늘어나 이 메서드에 실패 분기가 추가되더라도 로딩바를 숨기는 걸
        /// 깜빡할 여지가 없다.
        /// </summary>
        private async Awaitable SwapMapAsync(AddressableAssetKey _newMapKey, string _entryPointName)
        {
            if (AddressableAssetManager.Instance == null || localPlayerInstance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("SwapMap을 수행할 수 없습니다 (AddressableAssetManager 또는 플레이어가 준비되지 않음).");
                return;
            }

            if (isSwappingMap)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"이전 맵 전환이 아직 끝나지 않아 요청을 무시합니다. 요청한 맵 : {_newMapKey}");
                return;
            }

            isSwappingMap = true;
            GameManager.Instance?.ShowLoadingBar();

            // LoadingBarView는 ObjectPoolManager가 씬 전환 없이 재사용하는 인스턴스라, BootstrapSceneManager가
            // 마지막으로 남긴 타이틀("메인 씬으로 전환 준비 완료" 등)이 지워지지 않은 채 그대로 남아있다.
            // 맵 전환에는 그 문구가 맞지 않으므로 빈 문자열로 지운다.
            GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);

            try
            {
                string newMapKeyString = _newMapKey.ToString();

                // 지금 스폰돼있는 원격 플레이어/몬스터는 전부 이전 맵 소속이므로 미리 비운다.
                // 새 맵 목록은 Game_MapChangeAck 응답으로 다시 채워진다.
                RemotePlayerManager.Instance?.ClearAll();
                RemoteMonsterManager.Instance?.ClearAll();

                // ClearAll이 파괴한 몬스터를 UI_GameSceneView의 Update() 폴링(Unity null 비교)이 알아서
                // 감지하긴 하지만, 맵 전환 시점에 명시적으로 타겟 정보 패널을 즉시 닫아 경합 프레임을 없앤다.
                monsterTargetView?.ClearTarget();

                if (currentMapInstance != null)
                {
                    Destroy(currentMapInstance);
                    currentMapInstance = null;
                }

                const float loadTimeoutSeconds = 30f;
                float loadStartTime = Time.unscaledTime;

                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(newMapKeyString);

                // 새 맵을 로드하는 동안이 SwapMap 전체 소요 시간의 대부분을 차지하므로(나머지 단계는 전부 순간적),
                // "0%에 머물다 끝나면 100%로 점프"가 아니라 Addressables가 보고하는 실제 진행률을 그대로 반영한다.
                await AddressableAssetManager.Instance.WaitForLoadAsync(
                    newMapKeyString,
                    () => Time.unscaledTime - loadStartTime >= loadTimeoutSeconds,
                    percentComplete => GameManager.Instance?.LoadingBarView?.UpdateProgress(percentComplete));

                if (this == null || localPlayerInstance == null)
                {
                    return;
                }

                if (!AddressableAssetManager.Instance.GetHandler(newMapKeyString, out AsyncOperationHandle handle) || handle.Result is not GameObject prefab)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"맵 로드 실패 Key : {newMapKeyString}");
                    return;
                }

                currentMapInstance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                currentMapId = newMapKeyString;

                Transform entryPoint = FindChildRecursive(currentMapInstance.transform, _entryPointName);
                if (entryPoint == null)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"{newMapKeyString} 맵에서 {_entryPointName}을 찾을 수 없습니다.");
                    return;
                }

                if (localPlayerInstance.TryGetComponent(out Rigidbody rb))
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = entryPoint.position;
                    rb.rotation = entryPoint.rotation;
                }
                else
                {
                    localPlayerInstance.transform.SetPositionAndRotation(entryPoint.position, entryPoint.rotation);
                }

                GameServerConnectManager.Instance?.SendMapChange(newMapKeyString, entryPoint.position.x, entryPoint.position.y, entryPoint.position.z, entryPoint.eulerAngles.y);

                GameManager.Instance?.LoadingBarView?.UpdateProgress(1f);
            }
            finally
            {
                isSwappingMap = false;
                GameManager.Instance?.HideLoadingBar();
            }
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
                // 화살표 키로 이동/회전할 수 있게 된다.
                playerInstance.AddComponent<PlayerMoveController>();

                // 일정 주기로 자신의 위치/회전을 GameServer(Game_MoveRequest)로 전송한다.
                playerInstance.AddComponent<PlayerNetworkSender>();

                // Space 입력으로 전방의 원격 플레이어를 공격(Game_AttackRequest)한다.
                localPlayerAttackController = playerInstance.AddComponent<PlayerAttackController>();
                localPlayerAttackController.MonsterTargeted += HandleMonsterTargeted;
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

            // 이동/전투 카메라는 ThirdPersonFollow가 Follow 대상의 회전을 그대로 카메라 방향으로 쓰므로 LookAt은 필요 없다.
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
                AttackPower = spawnedPlayerModel != null ? spawnedPlayerModel.AttackPower : 0,
                Defense = spawnedPlayerModel != null ? spawnedPlayerModel.Defense : 0,
                Level = spawnedPlayerModel != null ? spawnedPlayerModel.Level : 1,
                Exp = spawnedPlayerModel != null ? spawnedPlayerModel.CurrentExp : 0
            };

            GameServerConnectManager.Instance.ConnectAndEnter(localInfo);

            // 다른 접속자의 입장/퇴장/이동, 몬스터 스폰/피격/사망 이벤트 구독을 시작한다
            // (최초 접근 시 SingletonObject가 자동 생성된다).
            _ = RemotePlayerManager.Instance;
            _ = RemoteMonsterManager.Instance;
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
                }

                // UI_InventoryView.Awake()가 임시 플레이스홀더 골드값으로 초기화해두므로, 생성 직후 실제
                // 세이브 데이터 값으로 즉시 덮어쓴다(그 사이 처치 보상을 먼저 받는 레이스는 없다 - 인벤토리는
                // 캐릭터 커스터마이징이 끝난 뒤에야 생성되고, GameServer 접속/전투는 그다음에 시작된다).
                RefreshInventoryDisplay();
            });
        }

        /// <summary>
        /// UI_GameSceneView(ChatContainer)에서 Enter로 전송한 메시지를 GameServer로 보낸다.
        /// 내 화면에는 여기서 직접 추가하지 않고, 서버가 되돌려주는 Game_ChatBroadcast(HandleChatReceived)를
        /// 통해 다른 접속자와 동일한 경로로 표시한다 - 메시지 순서/타임스탬프를 서버 기준으로 통일하기 위해서다.
        /// </summary>
        private void HandleChatMessageSubmitted(string message)
        {
            GameServerConnectManager.Instance?.SendChat(message);
        }

        /// <summary>
        /// GameServer로부터 받은 채팅(Game_ChatBroadcast, 내 메시지 포함)을 ChatContainer에 표시한다.
        /// </summary>
        private void HandleChatReceived(GameChatBroadcastPacket packet)
        {
            chatView?.AddChatMessage(packet.Nickname, packet.Message);
        }

        /// <summary>
        /// Game_DamageBroadcast는 전원(공격자 포함)에게 오지만, 이 메서드는 내(로컬 플레이어)가 맞은
        /// 경우만 처리한다. 다른 플레이어가 맞은 경우는 RemotePlayerManager가 별도로 구독해 처리한다.
        /// Damage는 방어력 적용 전 원본값이므로 spawnedPlayerModel.TakeDamage가 로컬 Defense로 직접 계산한다.
        /// </summary>
        private void HandleDamageReceived(GameDamageBroadcastPacket packet)
        {
            if (spawnedPlayerModel == null || SaveDataManager.Instance == null)
            {
                return;
            }

            if (packet.TargetId != SaveDataManager.Instance.SelectedCharacterId)
            {
                return;
            }

            spawnedPlayerModel.TakeDamage(new DamageInfo(packet.AttackerId, packet.Damage));
        }

        /// <summary>
        /// Game_MonsterAttackBroadcast는 전원에게 오지만(공격 애니메이션은 RemoteMonsterManager가 전원 재생),
        /// 실제 데미지 적용은 내(로컬 플레이어)가 대상인 경우만 처리한다 - HandleDamageReceived(PvP)와 동일한 패턴.
        /// </summary>
        private void HandleMonsterAttackReceived(GameMonsterAttackBroadcastPacket packet)
        {
            if (spawnedPlayerModel == null || SaveDataManager.Instance == null)
            {
                return;
            }

            if (packet.TargetPlayerId != SaveDataManager.Instance.SelectedCharacterId)
            {
                return;
            }

            spawnedPlayerModel.TakeDamage(new DamageInfo(packet.MonsterId, packet.Damage));
        }

        /// <summary>
        /// PlayerAttackController.MonsterTargeted(내가 몬스터를 공격할 때마다)를 그대로 UI_MonsterTargetView에
        /// 전달해 몬스터 이름/등급/체력바를 갱신하고, 타겟-로스트 타임아웃 판정에 쓸 시각을 갱신한다.
        /// </summary>
        private void HandleMonsterTargeted(RemoteMonsterController targetMonster)
        {
            monsterTargetView?.BindTarget(targetMonster);

            if (targetMonster != null)
            {
                lastMonsterTargetedTime = Time.time;
            }
        }

        /// <summary>
        /// 내가 몬스터를 처치해 GameServer가 계산한 경험치/레벨(Game_ExpGainBroadcast, 처치자 본인에게만 옴)을 반영하고,
        /// GameServer는 DB 접근 권한이 없으므로 MainServer(HTTP)에 직접 영속화를 요청한다.
        /// </summary>
        private void HandleExpGained(GameExpGainBroadcastPacket packet)
        {
            if (spawnedPlayerModel == null)
            {
                return;
            }

            spawnedPlayerModel.ApplyExpGain(packet.TotalExp, packet.Level, packet.ExpToNextLevel);
            SaveDataManager.Instance?.UpdateCharacterProgress(packet.Level, packet.TotalExp);
        }

        /// <summary>
        /// 내가 몬스터를 처치해 GameServer가 굴린 골드/아이템 드롭(Game_LootBroadcast, 처치자 본인에게만 옴)을 반영한다.
        /// HandleExpGained와 별개의 패킷이라(만렙이면 경험치 없이 드롭만 올 수 있음) 독립적으로 저장을 요청하되,
        /// 서버 kill-rewards 엔드포인트가 레벨/경험치도 함께 요구하므로 spawnedPlayerModel이 이미 들고 있는
        /// 현재 값을 그대로 다시 실어 보낸다(값이 바뀌지 않으므로 안전하게 덮어써진다).
        /// </summary>
        private void HandleLootReceived(GameLootBroadcastPacket packet)
        {
            if (spawnedPlayerModel == null)
            {
                return;
            }

            if (packet.GoldGained > 0)
            {
                spawnedPlayerModel.ApplyGoldGain(packet.GoldGained);
            }

            foreach (GameLootItemEntry item in packet.Items)
            {
                AddOrMergeInventoryItem(item.ItemId, item.Qty);
            }

            RefreshInventoryDisplay();

            ShowDropItemPopup(packet.GoldGained, packet.Items);

            SaveDataManager.Instance?.ApplyKillRewards(spawnedPlayerModel.Level, spawnedPlayerModel.CurrentExp, packet.GoldGained, packet.Items);
        }


        /// <summary>
        /// HandleLootReceived가 반영한 획득 골드/드롭 아이템 목록을 UI_DropItemPopupView로 보여준다.
        /// 아이템 없이 골드만 떨어진 경우에도 골드 줄만으로 팝업을 열어, 처치 보상은 항상 같은 팝업을 거치게 한다.
        /// RefreshInventoryDisplay와 동일하게 ItemDatabaseManager.FindById를 조회 함수로 넘긴다
        /// (아직 아이콘이 등록되지 않은 아이템은 UI_DropListItemView가 이름/수량만으로 최소 표시한다).
        /// </summary>
        private void ShowDropItemPopup(int goldGained, List<GameLootItemEntry> items)
        {
            int itemCount = items != null ? items.Count : 0;
            if (dropItemPopupView == null || (goldGained <= 0 && itemCount == 0))
            {
                return;
            }

            Func<string, ItemData> itemLookup = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById : null;
            dropItemPopupView.Show(goldGained, items, itemLookup);
        }


        /// <summary>
        /// localInventoryItems에서 같은 itemId 스택을 찾아 수량만 더하고, 없으면 새 스택을 추가한다.
        /// </summary>
        private void AddOrMergeInventoryItem(string itemId, int qty)
        {
            InventoryItemStack existing = localInventoryItems.Find(stack => stack.itemId == itemId);
            if (existing != null)
            {
                existing.count += qty;
                return;
            }

            localInventoryItems.Add(new InventoryItemStack(itemId, qty));
        }

        /// <summary>
        /// ItemDatabaseManager.OnDatabaseLoaded 콜백. 인벤토리가 ItemDatabaseSO 로드 완료 전에 먼저 열려
        /// 아이콘/이름 없이(itemId 텍스트만으로) 표시됐을 수 있으므로, 로드가 끝나는 즉시 한 번 더 갱신해
        /// 뒤늦게라도 정상 아이콘/이름으로 바뀌게 한다.
        /// </summary>
        private void HandleItemDatabaseLoaded()
        {
            // ItemDatabaseSO가 이제야 로드됐다면, 로드 전이라 건너뛰었던 장착 시각 반영/스탯 보너스 계산도 함께 재시도한다.
            ApplyEquippedVisuals();
            RecalculateEquipmentStats();
            RefreshInventoryDisplay();
        }

        /// <summary>
        /// 인벤토리 UI가 이미 생성되어 있으면 골드/아이템 슬롯과 스탯 패널을 현재 런타임 상태(spawnedPlayerModel)로
        /// 다시 그린다. 아이콘/등급은 ItemDatabaseManager를 통해 조회하며, 아직 로드되지 않았거나(부트스트랩 직후)
        /// 디자이너가 해당 itemId를 등록하기 전이면 UI_InventoryView가 알아서 아이콘 없이 최소 정보로 표시한다.
        /// 스탯 패널(공격력/방어력/최대체력)은 CombatStatComponent/HealthComponent가 이미 계산해둔 실제 값을
        /// 그대로 받아 표시하므로, 장비 보너스가 반영된 뒤(RecalculateEquipmentStats 이후) 호출해야 최신값이 보인다.
        /// </summary>
        private void RefreshInventoryDisplay()
        {
            if (inventoryView == null || spawnedPlayerModel == null)
            {
                return;
            }

            Func<string, ItemData> itemLookup = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById : null;
            inventoryView.RefreshInventory(spawnedPlayerModel.Gold, localInventoryItems, itemLookup);
            inventoryView.UpdateStatsUI(spawnedPlayerModel.Stats, spawnedPlayerModel.AttackPower, spawnedPlayerModel.Defense, spawnedPlayerModel.MaxHp);
        }

        /// <summary>
        /// localInventoryItems 중 장착 중인(equipSlot이 설정된) 스택들의 ItemData.bonusAttackPower/bonusDefense를
        /// 합산해 CombatStatComponent에 반영하고, GameServer 접속 중이면 갱신된 값을 알린다(Game_StatUpdateRequest).
        /// GameServer는 Game_EnterRequest 시점 스냅샷(PlayerInfo.AttackPower/Defense)을 그대로 캐싱해서 전투 판정에
        /// 쓰기 때문에(GameRoom.ApplyMonsterAttackAsync/AttackPlayerAsync), 이 알림이 없으면 인벤토리에는 스탯이
        /// 올랐다고 뜨지만 실제 몬스터 전투 데미지는 그대로인 불일치가 생긴다.
        /// 장착/해제(TryEquipItem/TryUnequipSlot)와 로그인 복원(ApplySelectedCharacterCustomization,
        /// HandleItemDatabaseLoaded) 양쪽에서 호출된다.
        /// </summary>
        private void RecalculateEquipmentStats()
        {
            if (spawnedPlayerModel == null || ItemDatabaseManager.Instance == null)
            {
                return;
            }

            int totalAttackBonus = 0;
            int totalDefenseBonus = 0;

            foreach (InventoryItemStack stack in localInventoryItems)
            {
                if (string.IsNullOrEmpty(stack.equipSlot))
                {
                    continue;
                }

                ItemData itemData = ItemDatabaseManager.Instance.FindById(stack.itemId);
                if (itemData == null)
                {
                    continue;
                }

                totalAttackBonus += itemData.bonusAttackPower;
                totalDefenseBonus += itemData.bonusDefense;
            }

            spawnedPlayerModel.SetEquipmentBonus(totalAttackBonus, totalDefenseBonus);

            GameServerConnectManager.Instance?.SendStatUpdate(spawnedPlayerModel.AttackPower, spawnedPlayerModel.Defense);
        }

        /// <summary>
        /// localInventoryItems 중 equipSlot이 설정된(장착 중인) 스택을 실제 캐릭터 장비 시각(PlayerCharacterModel.EquipItem)에
        /// 반영한다. 로그인 직후(캐릭터 복원, ApplySelectedCharacterCustomization)와 ItemDatabaseSO 로드 완료 시점
        /// (HandleItemDatabaseLoaded) 양쪽에서 호출된다 - 아이템 데이터베이스가 아직 로드되지 않은 상태에서 먼저
        /// 호출되면 해당 스택은 건너뛰고, 로드가 끝난 뒤 재호출로 뒤늦게 반영된다. EquipmentController.Equip은
        /// 멱등이라(같은 슬롯에 같은 비주얼을 다시 활성화) 두 번 호출돼도 안전하다.
        /// </summary>
        private void ApplyEquippedVisuals()
        {
            if (spawnedPlayerModel == null || ItemDatabaseManager.Instance == null)
            {
                return;
            }

            foreach (InventoryItemStack stack in localInventoryItems)
            {
                if (string.IsNullOrEmpty(stack.equipSlot))
                {
                    continue;
                }

                ItemData itemData = ItemDatabaseManager.Instance.FindById(stack.itemId);
                if (itemData != null)
                {
                    spawnedPlayerModel.EquipItem(itemData);
                }
            }
        }

        /// <summary>
        /// 인벤토리 슬롯의 "장착/사용" 또는 "장착 해제" 버튼 클릭(UI_InventoryView.OnUseItemRequested)을 처리한다.
        /// 클릭된 슬롯이 일반 인벤토리 칸이면 장비 아이템만 장착 처리하고(소비 아이템 사용은 아직 미구현),
        /// 장비 슬롯이면 장착을 해제한다.
        /// </summary>
        private void HandleInventoryUseRequested(UI_InventorySlot _slot)
        {
            if (_slot == null || !_slot.HasItem || spawnedPlayerModel == null)
            {
                return;
            }

            if (_slot.SlotType == InventorySlotType.Inventory)
            {
                TryEquipItem(_slot.ItemId);
            }
            else
            {
                TryUnequipSlot(ToEquipmentSlotType(_slot.SlotType));
            }
        }

        /// <summary>
        /// itemId를 장착한다. 장비 아이템이 아니면 조용히 무시한다(소비 아이템 "사용"은 별도 기능으로 남겨둔다).
        /// 로컬 상태를 먼저 낙관적으로 갱신해 UI/캐릭터 시각을 즉시 반영하고, 서버 저장은 백그라운드로 요청한다
        /// (HandleLootReceived 등 기존 인벤토리 갱신 흐름과 동일한 낙관적 갱신 패턴).
        /// </summary>
        private void TryEquipItem(string _itemId)
        {
            ItemData itemData = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById(_itemId) : null;
            if (itemData == null || itemData.itemType != ItemType.Eqiupment || itemData.equipSlotType == EquipmentSlotType.None)
            {
                return;
            }

            InventoryItemStack targetStack = localInventoryItems.Find(stack => stack.itemId == _itemId && string.IsNullOrEmpty(stack.equipSlot));
            if (targetStack == null)
            {
                return;
            }

            string slotKey = itemData.equipSlotType.ToString();
            InventoryItemStack previouslyEquipped = localInventoryItems.Find(stack => stack.equipSlot == slotKey);
            if (previouslyEquipped == targetStack)
            {
                return; // 이미 장착 중
            }

            if (previouslyEquipped != null)
            {
                previouslyEquipped.equipSlot = null;
            }

            targetStack.equipSlot = slotKey;

            spawnedPlayerModel.EquipItem(itemData);
            RecalculateEquipmentStats();
            RefreshInventoryDisplay();

            SaveDataManager.Instance?.EquipItem(_itemId, itemData.equipSlotType, success =>
            {
                if (!success)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"장비 장착 저장 실패 : {_itemId}");
                }
            });
        }

        /// <summary>
        /// 지정한 장비 슬롯을 해제한다. TryEquipItem과 대칭되는 낙관적 갱신 흐름을 따른다.
        /// </summary>
        private void TryUnequipSlot(EquipmentSlotType _slotType)
        {
            if (_slotType == EquipmentSlotType.None)
            {
                return;
            }

            string slotKey = _slotType.ToString();
            InventoryItemStack equippedStack = localInventoryItems.Find(stack => stack.equipSlot == slotKey);
            if (equippedStack == null)
            {
                return;
            }

            equippedStack.equipSlot = null;

            spawnedPlayerModel.UnequipItem(_slotType);
            RecalculateEquipmentStats();
            RefreshInventoryDisplay();

            SaveDataManager.Instance?.UnequipItem(_slotType, success =>
            {
                if (!success)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"장비 해제 저장 실패 : {_slotType}");
                }
            });
        }

        /// <summary>
        /// UI_InventorySlot.InventorySlotType(뷰 계층의 슬롯 종류)을 EquipmentSlotType(모델 계층의 장비 슬롯 종류)으로
        /// 변환한다. 일반 인벤토리 칸(Inventory)이면 장비 슬롯이 아니므로 None을 반환한다.
        /// </summary>
        private static EquipmentSlotType ToEquipmentSlotType(InventorySlotType _slotType) => _slotType switch
        {
            InventorySlotType.EquipmentWeapon => EquipmentSlotType.Weapon,
            InventorySlotType.EquipmentArmor => EquipmentSlotType.Armor,
            InventorySlotType.EquipmentHelmet => EquipmentSlotType.Helmet,
            InventorySlotType.EquipmentBoots => EquipmentSlotType.Boots,
            InventorySlotType.EquipmentAccessory => EquipmentSlotType.Accessory,
            _ => EquipmentSlotType.None
        };

        /// <summary>
        /// GameServer가 Game_EnterRequest 인증 실패 등으로 연결을 끊기 직전에 보낸 사유(System_Error)를 알림 팝업으로 보여준다.
        /// </summary>
        private void HandleGameServerError(string message)
        {
            GameManager.Instance?.ShowAlarmPopup("서버 오류", message);
        }

        /// <summary>
        /// 하트비트 타임아웃 등으로 GameServer 연결이 예기치 않게 끊어졌을 때(씬 전환 등으로 직접 Disconnect()를
        /// 호출한 경우는 포함되지 않음) 알림 팝업으로 사용자에게 알린다.
        /// </summary>
        private void HandleGameServerDisconnected()
        {
            GameManager.Instance?.ShowAlarmPopup("연결 끊김", "게임 서버와의 연결이 끊어졌습니다.");
        }

        /// <summary>
        /// logoutButton 클릭 시 호출된다. AccessToken이 아직 살아있는 동안 선택된 캐릭터의 마지막 접속시간부터
        /// 기록한 뒤(SaveDataManager.TouchLastLogin), 계정 세션(ServerConnectManager, Access/RefreshToken)을
        /// 종료하고 완료되면 LoginScene으로 전환한다. 순서를 바꿔 Logout을 먼저 하면 AccessToken이 지워져
        /// TouchLastLogin 요청이 401로 실패한다. GameServer(TCP) 연결은 씬 전환으로 GameScene이 언로드될 때
        /// OnDestroy에서 자동으로 끊기므로 여기서 별도로 처리하지 않는다.
        /// </summary>
        private void HandleLogoutButtonClicked()
        {
            if (isLoggingOut)
            {
                return;
            }

            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("ServerConnectManager.Instance가 null입니다.");
                return;
            }

            isLoggingOut = true;

            if (SaveDataManager.Instance != null && SaveDataManager.Instance.SelectedCharacterId.HasValue)
            {
                SaveDataManager.Instance.TouchLastLogin(_ => PerformLogout());
            }
            else
            {
                PerformLogout();
            }
        }

        private void PerformLogout()
        {
            ServerConnectManager.Instance.Logout(_ => TransitionToLoginScene());
        }

        private void TransitionToLoginScene()
        {
            isLoggingOut = false;

            if (SceneLoadManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("SceneLoadManager.Instance가 null입니다.");
                return;
            }

            SceneLoadManager.Instance.LoadSceneByTags("LoginScene");
        }

        /// <summary>
        /// I키 입력 시 호출된다. isInventoryActive를 기준으로 판단해 꺼져 있으면 켜고, 켜져 있으면 끈다.
        /// 인벤토리가 아직 생성되지 않았다면(SpawnInventoryUI 완료 전) 아무 것도 하지 않는다.
        /// </summary>
        private void ToggleInventory()
        {
            if (inventoryInstance == null)
            {
                return;
            }

            if (isInventoryActive)
            {
                inventoryInstance.SetActive(false);
                isInventoryActive = false;
            }
            else
            {
                // 반드시 SetActive(true)를 먼저 하고 그 다음에 갱신해야 한다. TextMeshPro/Image 등 UI Graphic은
                // 비활성 상태에서 텍스트/스프라이트를 바꿔도 내부적으로 SetVerticesDirty 등이 "IsActive()==false면
                // 무시"하기 때문에 다시 그려지도록 예약되지 않는다 - 그래서 활성화 전에 RefreshInventoryDisplay를
                // 먼저 호출하면(과거 코드) 처음 열 때는 골드/아이템이 비어 보이고, 한 번 껐다 켜야만(그 사이
                // 다른 경로로 한 번 더 갱신되며) 반영되는 문제가 있었다. 활성화부터 한 뒤에 갱신하면 첫 번째
                // 여는 시점부터 항상 정상적으로 보인다.
                inventoryInstance.SetActive(true);
                isInventoryActive = true;
                RefreshInventoryDisplay();
            }
        }

        #endregion
    }
}
