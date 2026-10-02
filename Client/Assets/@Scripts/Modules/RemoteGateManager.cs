using Incheol.Controller.Interaction;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// GameServerConnectManager가 수신한 던전 게이트 위치(Game_ActiveGateNotify)대로 게이트 프리팹을 생성한다. 후보 중 어느 곳에
    /// 게이트를 세울지는 서버가 방을 만들 때 한 곳만 정하고, 클라이언트는 알려준 id대로 만들기만 한다(RemoteChestManager와 같은 역할 분담).
    /// 생성 위치는 id와 이름이 같은 DungeonGateSpawnPointMarker(후보 마커)이며, 맵 프리팹 안에 있으므로 맵이 교체될 때 게이트도 함께 사라진다.
    /// 도착 맵은 맵 프리팹의 DungeonGateSpawnPlan에서 읽어 MapPortalController.SetTargetMap으로 넘긴다 - 서버도 같은 값을
    /// 내보낸 데이터(MapData/{mapId}.json의 gatePlan)로 맵 이동을 검증한다.
    /// 게이트는 방이 살아 있는 동안 위치가 바뀌지 않으므로(방 생성 시 1회 선정) 상자와 달리 리스폰/제거 알림은 없다.
    /// RemoteChestManager와 같은 패턴이다: GameScene 동안만 존재하면 되므로 PersistAcrossScenes를 쓰지 않는다.
    /// </summary>
    public class RemoteGateManager : SingletonObject<RemoteGateManager>
    {
        // Addressables에 등록된 게이트 프리팹 주소. MapPortalController(MapSwap)가 붙어 있다.
        private const string GatePrefabAddressableName = "PortalGate";

        // 지금 만들어 둔 게이트와 그 id. 같은 id의 알림이 다시 오면(재접속 등) 새로 만들지 않고 유지한다.
        private GameObject spawnedGate;
        private string spawnedGateId;

        // 프리팹을 불러오는 중인 게이트 id. 같은 id의 알림이 다시 와도 중복 생성하지 않는다.
        private string loadingGateId;

        // 서버가 마지막으로 알려준 게이트 id(없으면 null). 프리팹을 불러오는 사이 맵이 바뀌거나 ClearAll이 호출된 경우
        // 콜백이 뒤늦게 옛 맵의 게이트를 만들지 않도록 거르는 데 쓴다.
        private string activeGateId;

        #region LifeCycle
        private void OnEnable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnActiveGateReceived += HandleActiveGateReceived;
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnActiveGateReceived -= HandleActiveGateReceived;
        }
        #endregion

        #region Method
        private void HandleActiveGateReceived(GameActiveGatePacket packet)
        {
            // 서버가 알려준 게이트가 지금 것과 다르면(없음 포함) 먼저 정리한다. 같은 id면 그대로 둔다.
            string newId = packet.HasGate ? packet.Id : null;
            if (spawnedGate != null && spawnedGateId != newId)
            {
                DestroySpawnedGate();
            }

            activeGateId = newId;
            if (newId == null || spawnedGate != null || loadingGateId == newId)
            {
                return;
            }

            SpawnGate(packet);
        }

        private void SpawnGate(GameActiveGatePacket info)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteGateManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            loadingGateId = info.Id;
            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(GatePrefabAddressableName,
                prefab => OnPrefabLoaded(info, prefab),
                () => loadingGateId = null);
        }

        private void OnPrefabLoaded(GameActiveGatePacket info, GameObject prefab)
        {
            if (this == null)
            {
                return;
            }

            if (loadingGateId == info.Id)
            {
                loadingGateId = null;
            }

            // 로딩 중 맵이 바뀌었거나(ClearAll) 서버가 다른 게이트를 알려준 경우, 또는 중복 이벤트로 이미 만들어진 경우.
            if (prefab == null || activeGateId != info.Id || spawnedGate != null)
            {
                return;
            }

            // 도착 맵 설정이 없으면 게이트가 어디로도 못 간다 - 서버도 같은 이유로 후보를 내보내지 못하므로(MapDataExporter) 데이터가 어긋난 경우다.
            DungeonGateSpawnPlan plan = FindAnyObjectByType<DungeonGateSpawnPlan>(FindObjectsInactive.Include);
            if (plan == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteGateManager>("맵에 DungeonGateSpawnPlan이 없어 던전 게이트를 만들 수 없습니다.");
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
                DebugLogManager.GenerateErrorMessage<RemoteGateManager>($"게이트 후보 '{info.Id}'에 해당하는 마커를 씬에서 찾을 수 없어 서버 좌표에 생성합니다.");
                instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                instance.transform.position = new Vector3(info.X, info.Y, info.Z);
            }

            instance.name = $"DungeonGate_{info.Id}";

            MapPortalController portal = instance.GetComponent<MapPortalController>();
            if (portal == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteGateManager>($"'{GatePrefabAddressableName}' 프리팹에 MapPortalController가 없습니다.");
                Destroy(instance);
                return;
            }

            // 프리팹은 도착지가 비어 있는 템플릿이다 - 맵의 게이트 설정으로 채운다(SetTargetMap은 MapSwap 방식으로도 바꾼다).
            portal.SetTargetMap(plan.targetMapKey, plan.targetMapEntryPointName);

            spawnedGate = instance;
            spawnedGateId = info.Id;
        }

        /// <summary>
        /// id와 이름이 같은 DungeonGateSpawnPointMarker 오브젝트를 현재 씬에서 찾는다(MapDataExporter가 마커 이름을
        /// 그대로 후보 id로 내보냈다). 못 찾으면 null.
        /// </summary>
        private static Transform FindCandidateMarker(string id)
        {
            foreach (DungeonGateSpawnPointMarker marker in FindObjectsByType<DungeonGateSpawnPointMarker>(FindObjectsInactive.Include))
            {
                if (marker.gameObject.name == id)
                {
                    return marker.transform;
                }
            }

            return null;
        }

        private void DestroySpawnedGate()
        {
            if (spawnedGate != null)
            {
                Destroy(spawnedGate);
            }

            spawnedGate = null;
            spawnedGateId = null;
        }

        /// <summary>
        /// 맵 전환 시작/재접속 시작 시 호출한다. 지금 만들어 둔 게이트는 이전 방 기준이므로 비우고,
        /// 서버의 다음 Game_ActiveGateNotify(새 맵 또는 재입장)로 다시 채운다.
        /// </summary>
        public void ClearAll()
        {
            DestroySpawnedGate();
            loadingGateId = null;
            activeGateId = null;
        }
        #endregion
    }
}
