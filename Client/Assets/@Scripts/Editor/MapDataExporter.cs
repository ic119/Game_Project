using System.Collections.Generic;
using System.IO;
using Incheol.Controller.Interaction;
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
        private class MapDataJson
        {
            public PointJson respawnPoint;
            public List<PortalJson> portals = new();
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

                string json = JsonUtility.ToJson(fileData, true);
                string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ServerMapDataRelativePath));
                Directory.CreateDirectory(outputDirectory);
                string outputPath = Path.Combine(outputDirectory, $"{mapId}.json");
                File.WriteAllText(outputPath, json);

                Debug.Log($"[MapDataExporter] '{mapId}' 맵 데이터(포탈 {fileData.portals.Count}개)를 내보냈습니다 : {outputPath}");
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
            Collider collider = portal.GetComponentInChildren<Collider>(true);
            if (collider == null)
            {
                return 1f;
            }

            Vector3 extents = collider.bounds.extents;
            return Mathf.Max(extents.x, extents.z);
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
