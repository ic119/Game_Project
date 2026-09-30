using System.Collections.Generic;
using Incheol.Controller.Interaction;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// GameServerConnectManager가 수신한 활성 상자 목록(Game_ActiveChestsNotify)에서, 씬에 아직 없는 상자
    /// (서버가 후보 중에서 뽑은 상자)를 TreasureChest 프리팹으로 생성한다. 어느 후보에 상자를 세울지는 서버가 정하고
    /// 클라이언트는 알려준 id대로 만들기만 한다(RemoteMonsterManager와 같은 역할 분담).
    /// 고정 상자는 맵 프리팹에 이미 있으므로 id가 겹치면 건너뛴다. 생성 위치는 id와 이름이 같은
    /// TreasureChestSpawnPointMarker(후보 마커)이며, 맵 프리팹 안에 있으므로 맵이 교체될 때 상자도 함께 사라진다.
    /// RemotePlayerManager/RemoteMonsterManager와 같은 패턴이다: GameScene 동안만 존재하면 되므로 PersistAcrossScenes를 쓰지 않는다.
    /// </summary>
    public class RemoteChestManager : SingletonObject<RemoteChestManager>
    {
        private const string ChestPrefabAddressableName = "TreasureChest";

        private readonly Dictionary<string, TreasureChestInteractionController> spawnedChests = new();

        // 프리팹을 불러오는 중인 상자 id. 같은 id의 목록이 다시 와도 중복 생성하지 않는다.
        private readonly HashSet<string> loadingChestIds = new();

        // 서버가 마지막으로 알려준 활성 상자 id. 프리팹을 불러오는 사이 맵이 바뀌거나 ClearAll이 호출된 경우
        // 콜백이 뒤늦게 옛 맵의 상자를 만들지 않도록 거르는 데 쓴다.
        private readonly HashSet<string> activeChestIds = new();

        // 열림 알림(OnChestOpened)이 상자 생성보다 먼저 도착할 수 있다(프리팹 로딩이 비동기). 컨트롤러는 생성된 뒤에야
        // 이벤트를 구독하므로, 열린 id를 기억해 두었다가 생성 직후 반영한다.
        private readonly HashSet<string> openedChestIds = new();

        #region LifeCycle
        private void OnEnable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnActiveChestsReceived += HandleActiveChestsReceived;
            GameServerConnectManager.Instance.OnChestOpened += HandleChestOpened;
            GameServerConnectManager.Instance.OnChestSpawned += HandleChestSpawned;
            GameServerConnectManager.Instance.OnChestDespawned += HandleChestDespawned;
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnActiveChestsReceived -= HandleActiveChestsReceived;
            GameServerConnectManager.Instance.OnChestOpened -= HandleChestOpened;
            GameServerConnectManager.Instance.OnChestSpawned -= HandleChestSpawned;
            GameServerConnectManager.Instance.OnChestDespawned -= HandleChestDespawned;
        }
        #endregion

        #region Method
        private void HandleActiveChestsReceived(GameActiveChestsPacket packet)
        {
            activeChestIds.Clear();
            foreach (GameChestInfo info in packet.Chests)
            {
                activeChestIds.Add(info.Id);
            }

            HashSet<string> sceneChestIds = CollectSceneChestIds();
            foreach (GameChestInfo info in packet.Chests)
            {
                SpawnIfMissing(info, sceneChestIds);
            }
        }

        private void HandleChestOpened(GameChestOpenBroadcastPacket packet)
        {
            openedChestIds.Add(packet.ChestId);
        }

        /// <summary>
        /// 리스폰으로 새 상자가 생겼다(Game_ChestSpawnBroadcast). 입장 직후의 스냅샷과 같은 경로로 생성한다 -
        /// 이미 씬에 있거나 만들었거나 로딩 중인 id면(스냅샷과 겹친 경우) 건너뛴다.
        /// </summary>
        private void HandleChestSpawned(GameChestInfo info)
        {
            activeChestIds.Add(info.Id);
            SpawnIfMissing(info, CollectSceneChestIds());
        }

        /// <summary>
        /// 열린 뒤 잔존 시간이 지난 상자가 사라졌다(Game_ChestDespawnBroadcast). 상자를 지우고 "열림" 기록도 함께 지운다 -
        /// 같은 id의 상자가 나중에 다시 생기면 닫힌 상태여야 하기 때문이다. 프리팹을 불러오는 중이었다면 활성 id에서 빠졌으므로
        /// 로딩 콜백(OnPrefabLoaded)이 알아서 생성을 포기한다. 모르는 id(스냅샷보다 먼저 온 경우)는 조용히 무시한다.
        /// </summary>
        private void HandleChestDespawned(string chestId)
        {
            activeChestIds.Remove(chestId);
            openedChestIds.Remove(chestId);

            if (spawnedChests.TryGetValue(chestId, out TreasureChestInteractionController controller))
            {
                spawnedChests.Remove(chestId);
                if (controller != null)
                {
                    // Destroy는 프레임 끝에야 실제로 지워지므로, 같은 프레임에 같은 id의 새 상자가 오면(후보가 하나뿐일 때 제자리 리스폰)
                    // CollectSceneChestIds가 이 죽어가는 오브젝트를 "이미 있는 상자"로 착각해 새 상자를 건너뛴다 - id를 비워 막는다.
                    controller.Initialize(string.Empty);
                    Destroy(controller.gameObject);
                }
            }
        }

        // 씬에 이미 있는 상자(맵 프리팹의 고정 상자)의 id. 이 id는 다시 만들지 않는다.
        private static HashSet<string> CollectSceneChestIds()
        {
            var ids = new HashSet<string>();
            foreach (TreasureChestInteractionController chest in FindObjectsByType<TreasureChestInteractionController>(FindObjectsInactive.Include))
            {
                ids.Add(chest.ChestId);
            }

            return ids;
        }

        private void SpawnIfMissing(GameChestInfo info, HashSet<string> sceneChestIds)
        {
            if (sceneChestIds.Contains(info.Id) || spawnedChests.ContainsKey(info.Id) || loadingChestIds.Contains(info.Id))
            {
                return;
            }

            SpawnChest(info);
        }

        private void SpawnChest(GameChestInfo info)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteChestManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            loadingChestIds.Add(info.Id);
            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(ChestPrefabAddressableName,
                prefab => OnPrefabLoaded(info, prefab),
                () => loadingChestIds.Remove(info.Id));
        }

        private void OnPrefabLoaded(GameChestInfo info, GameObject prefab)
        {
            if (this == null)
            {
                return;
            }

            loadingChestIds.Remove(info.Id);

            // 로딩 중 맵이 바뀌었거나(ClearAll) 새 목록에서 빠진 상자, 또는 중복 이벤트로 이미 만들어진 상자.
            if (prefab == null || !activeChestIds.Contains(info.Id) || spawnedChests.ContainsKey(info.Id))
            {
                return;
            }

            // 후보 마커가 부모면 마커의 위치/회전을 그대로 따른다. 맵 프리팹 안에 있어 맵과 함께 정리되기도 한다.
            // 못 찾으면(맵 데이터와 프리팹이 어긋난 경우) 서버가 알려준 좌표에 이 매니저 밑으로 만든다.
            Transform marker = FindCandidateMarker(info.Id);
            GameObject instance;
            if (marker != null)
            {
                instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, marker);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }
            else
            {
                DebugLogManager.GenerateErrorMessage<RemoteChestManager>($"상자 후보 '{info.Id}'에 해당하는 마커를 씬에서 찾을 수 없어 서버 좌표에 생성합니다.");
                instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                instance.transform.position = new Vector3(info.X, info.Y, info.Z);
            }

            instance.name = $"Chest_{info.Id}";

            TreasureChestInteractionController controller = instance.GetComponent<TreasureChestInteractionController>();
            if (controller == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteChestManager>("TreasureChest 프리팹에 TreasureChestInteractionController가 없습니다.");
                Destroy(instance);
                return;
            }

            controller.Initialize(info.Id);
            spawnedChests[info.Id] = controller;

            if (openedChestIds.Contains(info.Id))
            {
                controller.ApplyOpenedState();
            }
        }

        /// <summary>
        /// id와 이름이 같은 TreasureChestSpawnPointMarker 오브젝트를 현재 씬에서 찾는다(MapDataExporter가 마커 이름을
        /// 그대로 후보 id로 내보냈다). 못 찾으면 null.
        /// </summary>
        private static Transform FindCandidateMarker(string id)
        {
            foreach (TreasureChestSpawnPointMarker marker in FindObjectsByType<TreasureChestSpawnPointMarker>(FindObjectsInactive.Include))
            {
                if (marker.gameObject.name == id)
                {
                    return marker.transform;
                }
            }

            return null;
        }

        /// <summary>
        /// 맵 전환 시작/재접속 시작 시 호출한다. 지금 생성해 둔 상자는 전부 이전 방 기준이므로 비우고,
        /// 서버의 다음 Game_ActiveChestsNotify(새 맵 또는 재입장 목록)로 다시 채운다.
        /// </summary>
        public void ClearAll()
        {
            foreach (TreasureChestInteractionController controller in spawnedChests.Values)
            {
                if (controller != null)
                {
                    Destroy(controller.gameObject);
                }
            }

            spawnedChests.Clear();
            loadingChestIds.Clear();
            activeChestIds.Clear();
            openedChestIds.Clear();
        }
        #endregion
    }
}
