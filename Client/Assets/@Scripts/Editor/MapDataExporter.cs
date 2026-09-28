using System.IO;
using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    /// <summary>
    /// 맵 프리팹에서 서버가 알아야 하는 좌표 데이터(현재는 RespawnPoint)를 서버가 읽는 MapData/{mapId}.json
    /// 형식으로 내보낸다. GameServer는 이 값으로 입장/부활 위치를 직접 정한다 - 클라이언트가 보낸 좌표로
    /// 순간이동을 허용하면 이동 속도 검증(ClientSession.HandleMoveRequestAsync)을 우회할 수 있기 때문이다.
    /// mapId와 출력 폴더 규칙은 MonsterSpawnPointExporter와 동일하다(프리팹 파일명 = mapId).
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
        private class MapDataJson
        {
            public PointJson respawnPoint;
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
                    string message = $"'{mapId}' 프리팹 안에 '{RespawnPointName}' 오브젝트가 없습니다.";
                    if (showDialogs) EditorUtility.DisplayDialog("맵 데이터 내보내기", message, "확인");
                    Debug.LogError($"[MapDataExporter] {message} 내보내기를 중단합니다.");
                    return false;
                }

                var fileData = new MapDataJson { respawnPoint = ToPointJson(respawnPoint) };

                string json = JsonUtility.ToJson(fileData, true);
                string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ServerMapDataRelativePath));
                Directory.CreateDirectory(outputDirectory);
                string outputPath = Path.Combine(outputDirectory, $"{mapId}.json");
                File.WriteAllText(outputPath, json);

                Debug.Log($"[MapDataExporter] '{mapId}' 맵 데이터를 내보냈습니다 : {outputPath}");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
