using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class GameSceneManager : MonoBehaviour
    {
        #region Variable
        private const string gameSceneTag = "GameScene";

        private UI_GameSceneView gameSceneView;
        private PlayerCharacterModel spawnedPlayerModel;

        private GameObject inventoryInstance;

        /// <summary>
        /// 현재 로드되어 있는 맵 프리팹 인스턴스와 그 Addressable 키.
        /// MapPortalController(PortalTeleportType.MapSwap)가 SwapMap을 호출할 때 이전 맵을 정리하는 데 쓴다.
        /// </summary>
        private GameObject currentMapInstance;
        private string currentMapId = nameof(AddressableAssetKey.Farm);

        /// <summary>
        /// 로컬 플레이어 인스턴스. SpawnPlayerCharacter에서 respawnPoint의 자식으로 생성되므로,
        /// 맵 전환 시 맵 프리팹이 파괴되기 전에 분리(SetParent)해서 함께 파괴되지 않도록 해야 한다.
        /// </summary>
        private GameObject localPlayerInstance;

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
                GameServerConnectManager.Instance.OnServerError += HandleGameServerError;
                GameServerConnectManager.Instance.OnDisconnected += HandleGameServerDisconnected;
            }
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnChatReceived -= HandleChatReceived;
                GameServerConnectManager.Instance.OnDamageReceived -= HandleDamageReceived;
                GameServerConnectManager.Instance.OnServerError -= HandleGameServerError;
                GameServerConnectManager.Instance.OnDisconnected -= HandleGameServerDisconnected;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleInventory();
            }
        }

        private void OnDestroy()
        {
            if (gameSceneView != null)
            {
                gameSceneView.ChatMessageSubmitted -= HandleChatMessageSubmitted;
                gameSceneView.LogoutButtonClicked -= HandleLogoutButtonClicked;
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

                    // Farm은 단순 프리로드 대상이 아니라 플레이어가 실제로 배치될 맵이므로,
                    // 생성 직후 맵 안의 RespawnPoint를 찾아 그 자리에 선택된 캐릭터를 스폰한다.
                    if (key == AddressableAssetKey.Farm)
                    {
                        currentMapInstance = instance;
                        currentMapId = keyString;
                        SpawnPlayerAtRespawnPoint(instance);
                    }

                    // UI_GameScene과 플레이어(Farm 하위에서 비동기로 스폰됨)는 로드 완료 순서가 보장되지 않으므로,
                    // 둘 다 준비된 시점에 TryBindPlayerInfo가 캐릭터 정보를 UI에 반영한다.
                    if (key == AddressableAssetKey.UI_GameScene)
                    {
                        instance.TryGetComponent(out gameSceneView);

                        if (gameSceneView != null)
                        {
                            gameSceneView.ChatMessageSubmitted += HandleChatMessageSubmitted;
                            gameSceneView.LogoutButtonClicked += HandleLogoutButtonClicked;
                        }

                        TryBindPlayerInfo();
                    }
                });
            }
        }

        /// <summary>
        /// Farm 맵 인스턴스 하위에서 "RespawnPoint" 이름의 Transform을 찾아 그 위치에 플레이어 캐릭터를 스폰한다.
        /// </summary>
        private void SpawnPlayerAtRespawnPoint(GameObject _farmInstance)
        {
            if (_farmInstance == null)
            {
                return;
            }

            Transform respawnPoint = FindChildRecursive(_farmInstance.transform, "RespawnPoint");

            if (respawnPoint == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("Farm 맵에서 RespawnPoint를 찾을 수 없습니다.");
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
        /// </summary>
        public void SwapMap(AddressableAssetKey _newMapKey, string _entryPointName = "RespawnPoint")
        {
            if (AddressableAssetManager.Instance == null || localPlayerInstance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("SwapMap을 수행할 수 없습니다 (AddressableAssetManager 또는 플레이어가 준비되지 않음).");
                return;
            }

            string newMapKeyString = _newMapKey.ToString();

            // 맵 프리팹이 파괴되기 전에 플레이어를 먼저 분리한다 - RespawnPoint 하위에 있으므로 같이 파괴되는 걸 막는다.
            localPlayerInstance.transform.SetParent(transform, true);

            // 지금 스폰돼있는 원격 플레이어는 전부 이전 맵 소속이므로 미리 비운다.
            // 새 맵 목록은 Game_MapChangeAck 응답으로 다시 채워진다.
            RemotePlayerManager.Instance?.ClearAll();

            if (currentMapInstance != null)
            {
                Destroy(currentMapInstance);
                currentMapInstance = null;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(newMapKeyString, prefab =>
            {
                if (this == null || localPlayerInstance == null)
                {
                    return;
                }

                if (prefab == null)
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
            });
        }

        /// <summary>
        /// BasicCharacter를 Addressable로 로드해 _respawnPoint의 자식으로 생성하고,
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

                GameObject playerInstance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, _respawnPoint);
                playerInstance.transform.localPosition = Vector3.zero;
                playerInstance.transform.localRotation = Quaternion.identity;
                localPlayerInstance = playerInstance;

                ApplySelectedCharacterCustomization(playerInstance);
                AssignPlayerToFollowCamera(playerInstance.transform);

                // RequireComponent로 Rigidbody/CapsuleCollider가 함께 추가되어, 스폰 직후 중력을 받아 지면에 착지하고
                // 화살표 키로 이동/회전할 수 있게 된다.
                playerInstance.AddComponent<PlayerMoveController>();

                // 일정 주기로 자신의 위치/회전을 GameServer(Game_MoveRequest)로 전송한다.
                playerInstance.AddComponent<PlayerNetworkSender>();

                // Space 입력으로 전방의 원격 플레이어를 공격(Game_AttackRequest)한다.
                playerInstance.AddComponent<PlayerAttackController>();
            });
        }

        /// <summary>
        /// 씬의 CinemachineCamera(CM_PlayerFollowCamera)가 방금 스폰된 플레이어를 추적하도록 Follow 타깃을 연결한다.
        /// Farm/RespawnPoint 등 씬 구성 에셋과 달리 카메라는 GameScene.unity에 이미 배치되어 있으므로 여기서는 찾아서 연결만 한다.
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
                Defense = spawnedPlayerModel != null ? spawnedPlayerModel.Defense : 0
            };

            GameServerConnectManager.Instance.ConnectAndEnter(localInfo);

            // 다른 접속자의 입장/퇴장/이동 이벤트 구독을 시작한다(최초 접근 시 SingletonObject가 자동 생성된다).
            _ = RemotePlayerManager.Instance;
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
            }
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
            gameSceneView?.AddChatMessage(packet.Nickname, packet.Message);
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
                inventoryInstance.SetActive(true);
                isInventoryActive = true;
            }
        }

        #endregion
    }
}
