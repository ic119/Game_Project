using System.Collections.Generic;
using System.IO;
using Incheol.Controller.Interaction;
using Incheol.Models.Define;
using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    /// <summary>
    /// 맵 프리팹에서 서버가 알아야 하는 좌표 데이터(RespawnPoint, 포탈)를 서버가 읽는 MapData/{mapId}.json
    /// 형식으로 내보낸다. GameServer는 이 값으로 입장/부활 위치를 직접 정하고, 맵 이동/좌표 이동 포탈을 검증한다 -
    /// 클라이언트가 보낸 좌표로 순간이동을 허용하면 이동 속도 검증(ClientSession.HandleMoveRequestAsync)을 우회할 수 있기 때문이다.
    /// mapId와 출력 폴더 규칙은 MonsterSpawnPointExporter와 동일하다(프리팹 파일명 = mapId = AddressableAssetKey 이름).
    /// 포탈의 목적지는 인스펙터에 설정된 값만 내보낸다 - 실행 중 SetTargetMap/SetTargetDestination으로 바꾼 목적지는
    /// 서버가 알 수 없어 거부된다.
    /// </summary>
    public static class MapDataExporter
    {
        private const string ServerMapDataRelativePath = "../../Server/MainServer/GameServer/MapData";
        private const string RespawnPointName = "RespawnPoint";

        [System.Serializable]
        private class PointJson
        {
            public float x;
            public float y;
            public float z;
            public float rotationY;
        }

        [System.Serializable]
        private class PortalJson
        {
            public float x;
            public float y;
            public float z;

            // 포탈 콜라이더의 수평 반경(m). 서버는 여기에 여유 거리를 더해 "포탈 안에 있었는지"를 판정한다.
            public float radius;

            // PortalTeleportType 이름 그대로("MapSwap" / "CoordinateTeleport").
            public string type;

            // MapSwap일 때 도착 맵 id(AddressableAssetKey 이름). CoordinateTeleport면 빈 문자열.
            public string targetMapId;

            // 도착 위치. MapSwap이면 도착 맵 프리팹 안의 진입 지점, CoordinateTeleport면 같은 맵 안의 목적지.
            public PointJson destination;
        }

        [System.Serializable]
        private class ChestJson
        {
            public string id;
            public float x;
            public float y;
            public float z;

            // 상자 콜라이더(UI_InteractionPrompt의 트리거)의 수평 반경(m). 서버는 여기에 여유 거리를 더해 판정한다.
            public float radius;

            // Drops/DropTables.json의 키(TreasureChestInteractionController.lootTableKey 그대로).
            public string lootTableKey;
        }

        // 상자가 설 수 있는 후보 지점. 서버가 chestSpawnCounts[]의 개수만큼 같은 lootTableKey 후보 중에서 뽑는다.
        [System.Serializable]
        private class ChestCandidateJson
        {
            // 후보 마커 GameObject 이름(맵 안에서 고유). 서버가 뽑은 후보의 chestId로 그대로 쓸 수 있다.
            public string id;
            public float x;
            public float y;
            public float z;
            public float radius;
            public string lootTableKey;
        }

        [System.Serializable]
        private class ChestSpawnCountJson
        {
            public string lootTableKey;
            public int count;
        }

        [System.Serializable]
        private class MapDataJson
        {
            public PointJson respawnPoint;
            public List<PortalJson> portals = new();
            public List<ChestJson> chests = new();
            public List<ChestCandidateJson> chestCandidates = new();
            public List<ChestSpawnCountJson> chestSpawnCounts = new();
        }

        [MenuItem("Tools/Map/Export Map Data From Selected Prefab")]
        private static void ExportFromSelection()
        {
            GameObject selected = Selection.activeGameObject;
            string assetPath = selected != null ? AssetDatabase.GetAssetPath(selected) : null;

            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab"))
            {
                EditorUtility.DisplayDialog("맵 데이터 내보내기", "Project 창에서 맵 프리팹(예: Floor001)을 선택한 상태에서 실행하세요.", "확인");
                return;
            }

            ExportPrefabAtPath(assetPath, showDialogs: true);
        }

        // 메뉴 진입점(ExportFromSelection)과 자동화 실행이 공유하는 실제 내보내기 로직.
        // showDialogs=false면 팝업 없이 조용히 동작한다. 성공하면 true.
        public static bool ExportPrefabAtPath(string assetPath, bool showDialogs)
        {
            string mapId = Path.GetFileNameWithoutExtension(assetPath);

            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                Transform respawnPoint = FindChildRecursive(root.transform, RespawnPointName);
                if (respawnPoint == null)
                {
                    // 부활/입장 위치 없이 서버가 뜨면 그 맵에서는 순간이동을 검증할 기준이 없다 - 파일을 쓰지 않고 중단한다.
                    return Fail(showDialogs, $"'{mapId}' 프리팹 안에 '{RespawnPointName}' 오브젝트가 없습니다.");
                }

                var fileData = new MapDataJson { respawnPoint = ToPointJson(respawnPoint) };

                foreach (MapPortalController portal in root.GetComponentsInChildren<MapPortalController>(true))
                {
                    if (!TryBuildPortalJson(portal, out PortalJson portalJson, out string error))
                    {
                        // 목적지를 알 수 없는 포탈을 빼고 내보내면 그 포탈만 조용히 "항상 거부"가 된다 - 통째로 중단한다.
                        return Fail(showDialogs, $"'{mapId}'의 포탈 '{portal.gameObject.name}' : {error}");
                    }

                    fileData.portals.Add(portalJson);
                }

                var seenChestIds = new HashSet<string>();
                foreach (TreasureChestInteractionController chest in root.GetComponentsInChildren<TreasureChestInteractionController>(true))
                {
                    if (!TryBuildChestJson(chest, out ChestJson chestJson, out string error))
                    {
                        return Fail(showDialogs, $"'{mapId}'의 상자 '{chest.gameObject.name}' : {error}");
                    }

                    if (!seenChestIds.Add(chestJson.id))
                    {
                        return Fail(showDialogs, $"'{mapId}'에 중복된 상자 id가 있습니다 : {chestJson.id}");
                    }

                    fileData.chests.Add(chestJson);
                }

                if (!TryBuildChestCandidates(root, fileData, seenChestIds, out string candidateError))
                {
                    return Fail(showDialogs, $"'{mapId}' : {candidateError}");
                }

                string json = JsonUtility.ToJson(fileData, true);
                string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ServerMapDataRelativePath));
                Directory.CreateDirectory(outputDirectory);
                string outputPath = Path.Combine(outputDirectory, $"{mapId}.json");
                File.WriteAllText(outputPath, json);

                Debug.Log($"[MapDataExporter] '{mapId}' 맵 데이터(포탈 {fileData.portals.Count}개, 상자 {fileData.chests.Count}개, 상자 후보 {fileData.chestCandidates.Count}개)를 내보냈습니다 : {outputPath}");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool Fail(bool showDialogs, string message)
        {
            if (showDialogs) EditorUtility.DisplayDialog("맵 데이터 내보내기", message, "확인");
            Debug.LogError($"[MapDataExporter] {message} 내보내기를 중단합니다.");
            return false;
        }

        // MapPortalController의 설정 필드는 private [SerializeField]라 SerializedObject로 읽는다.
        private static bool TryBuildPortalJson(MapPortalController portal, out PortalJson portalJson, out string error)
        {
            portalJson = null;
            var serialized = new SerializedObject(portal);
            SerializedProperty typeProperty = serialized.FindProperty("teleportType");
            string type = typeProperty.enumNames[typeProperty.enumValueIndex];

            PointJson destination;
            string targetMapId = string.Empty;

            if (type == nameof(PortalTeleportType.MapSwap))
            {
                SerializedProperty mapKeyProperty = serialized.FindProperty("targetMapKey");
                targetMapId = mapKeyProperty.enumNames[mapKeyProperty.enumValueIndex];
                string entryPointName = serialized.FindProperty("targetMapEntryPointName").stringValue;

                if (!TryFindEntryPointInMap(targetMapId, entryPointName, out destination, out error))
                {
                    return false;
                }
            }
            else
            {
                var destinationTransform = serialized.FindProperty("targetDestination").objectReferenceValue as Transform;
                if (destinationTransform != null)
                {
                    destination = ToPointJson(destinationTransform);
                }
                else
                {
                    // MapPortalController.ExecuteTeleport와 같은 규칙: 목표 Transform이 없으면 고정 좌표, 회전은 유지.
                    Vector3 coordinate = serialized.FindProperty("targetCoordinate").vector3Value;
                    destination = new PointJson { x = coordinate.x, y = coordinate.y, z = coordinate.z, rotationY = portal.transform.eulerAngles.y };
                }
            }

            Vector3 position = portal.transform.position;
            portalJson = new PortalJson
            {
                x = position.x,
                y = position.y,
                z = position.z,
                radius = GetHorizontalRadius(portal),
                type = type,
                targetMapId = targetMapId,
                destination = destination
            };
            error = null;
            return true;
        }

        // 도착 맵 프리팹(파일명 = mapId)을 열어 진입 지점 좌표를 읽는다. GameSceneManager.SwapMapAsync가 도착 후
        // 같은 이름으로 찾는 Transform이다.
        private static bool TryFindEntryPointInMap(string targetMapId, string entryPointName, out PointJson entryPoint, out string error)
        {
            entryPoint = null;

            string targetPath = null;
            foreach (string guid in AssetDatabase.FindAssets($"{targetMapId} t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == targetMapId)
                {
                    targetPath = path;
                    break;
                }
            }

            if (targetPath == null)
            {
                error = $"도착 맵 프리팹 '{targetMapId}'을(를) 찾을 수 없습니다.";
                return false;
            }

            GameObject targetRoot = PrefabUtility.LoadPrefabContents(targetPath);
            try
            {
                Transform entry = FindChildRecursive(targetRoot.transform, entryPointName);
                if (entry == null)
                {
                    error = $"도착 맵 '{targetMapId}' 안에 진입 지점 '{entryPointName}'이(가) 없습니다.";
                    return false;
                }

                entryPoint = ToPointJson(entry);
                error = null;
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(targetRoot);
            }
        }

        // 포탈 콜라이더(Portal.prefab은 CapsuleCollider)의 월드 bounds에서 수평 반경을 구한다. 콜라이더가 없으면 1m.
        private static float GetHorizontalRadius(MapPortalController portal)
        {
            return GetHorizontalRadius(portal.GetComponentInChildren<Collider>(true));
        }

        // TreasureChestInteractionController가 요구하는 UI_InteractionPrompt의 콜라이더 반경을 그대로 쓴다.
        private static float GetHorizontalRadius(Collider collider)
        {
            if (collider == null)
            {
                return 1f;
            }

            Vector3 extents = collider.bounds.extents;
            return Mathf.Max(extents.x, extents.z);
        }

        // TreasureChestInteractionController의 chestId/lootTableKey는 private [SerializeField]라 SerializedObject로 읽는다.
        private static bool TryBuildChestJson(TreasureChestInteractionController chest, out ChestJson chestJson, out string error)
        {
            chestJson = null;
            var serialized = new SerializedObject(chest);
            string id = serialized.FindProperty("chestId").stringValue;
            string lootTableKey = serialized.FindProperty("lootTableKey").stringValue;

            if (string.IsNullOrEmpty(id))
            {
                error = "chestId가 비어 있습니다(인스펙터에서 고유 id를 지정하세요).";
                return false;
            }

            if (string.IsNullOrEmpty(lootTableKey))
            {
                error = "lootTableKey가 비어 있습니다.";
                return false;
            }

            // UI_InteractionPrompt(같은 오브젝트에 RequireComponent로 붙어 있음)의 트리거 콜라이더 반경을 그대로 쓴다 -
            // "언제 프롬프트가 뜨는지"와 "언제 서버가 사거리 안으로 인정하는지"가 어긋나면 안 되기 때문이다.
            Collider collider = chest.GetComponent<Collider>();
            if (collider == null)
            {
                error = "UI_InteractionPrompt의 콜라이더를 찾지 못했습니다.";
                return false;
            }

            Vector3 position = chest.transform.position;
            chestJson = new ChestJson
            {
                id = id,
                x = position.x,
                y = position.y,
                z = position.z,
                radius = GetHorizontalRadius(collider),
                lootTableKey = lootTableKey
            };
            error = null;
            return true;
        }

        // 후보 마커(TreasureChestSpawnPointMarker)와 등급별 개수(TreasureChestSpawnPlan)를 검증해 내보낸다.
        // 후보 id는 고정 상자 id(usedIds)와도 겹치면 안 된다 - 서버가 둘 다 chestId로 취급하기 때문이다.
        // 잘못된 개수를 그대로 내보내면 서버가 "뽑을 후보가 모자란" 상태로 뜨므로 통째로 중단한다.
        private static bool TryBuildChestCandidates(GameObject root, MapDataJson fileData, HashSet<string> usedIds, out string error)
        {
            TreasureChestSpawnPointMarker[] markers = root.GetComponentsInChildren<TreasureChestSpawnPointMarker>(true);
            TreasureChestSpawnPlan plan = root.GetComponentInChildren<TreasureChestSpawnPlan>(true);
            var candidateCountByKey = new Dictionary<ChestLootTableKey, int>();

            foreach (TreasureChestSpawnPointMarker marker in markers)
            {
                string id = marker.gameObject.name;
                if (!usedIds.Add(id))
                {
                    error = $"상자 후보 '{id}'의 이름이 다른 상자/후보와 중복됩니다(후보 이름이 곧 id입니다).";
                    return false;
                }

                if (marker.lootTableKey == ChestLootTableKey.None)
                {
                    error = $"상자 후보 '{id}'의 lootTableKey가 None입니다.";
                    return false;
                }

                Vector3 position = marker.transform.position;
                fileData.chestCandidates.Add(new ChestCandidateJson
                {
                    id = id,
                    x = position.x,
                    y = position.y,
                    z = position.z,
                    radius = marker.interactRadius,
                    lootTableKey = marker.lootTableKey.ToString()
                });

                candidateCountByKey.TryGetValue(marker.lootTableKey, out int current);
                candidateCountByKey[marker.lootTableKey] = current + 1;
            }

            if (markers.Length > 0 && plan == null)
            {
                error = "상자 후보가 있는데 TreasureChestSpawnPlan 컴포넌트가 없습니다(맵 프리팹에 추가해 등급별 개수를 지정하세요).";
                return false;
            }

            if (plan != null)
            {
                var seenKeys = new HashSet<ChestLootTableKey>();
                foreach (TreasureChestSpawnCount entry in plan.counts)
                {
                    if (entry.lootTableKey == ChestLootTableKey.None || !seenKeys.Add(entry.lootTableKey))
                    {
                        error = $"TreasureChestSpawnPlan의 lootTableKey '{entry.lootTableKey}'가 None이거나 중복됩니다.";
                        return false;
                    }

                    candidateCountByKey.TryGetValue(entry.lootTableKey, out int available);
                    if (entry.count > available)
                    {
                        error = $"lootTableKey '{entry.lootTableKey}'의 뽑을 개수({entry.count})가 후보 수({available})보다 많습니다.";
                        return false;
                    }

                    fileData.chestSpawnCounts.Add(new ChestSpawnCountJson { lootTableKey = entry.lootTableKey.ToString(), count = entry.count });
                }
            }

            error = null;
            return true;
        }

        private static PointJson ToPointJson(Transform transform)
        {
            Vector3 position = transform.position;
            return new PointJson { x = position.x, y = position.y, z = position.z, rotationY = transform.eulerAngles.y };
        }

        // GameSceneManager.FindChildRecursive와 같은 이름 기반 탐색 - 클라이언트가 RespawnPoint를 찾는 방식과 맞춘다.
        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        [MenuItem("Tools/Map/Export Map Data From Selected Prefab", true)]
        private static bool ValidateExportFromSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(selected);
            return !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab");
        }
    }
}
