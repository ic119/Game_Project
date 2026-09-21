using System.Collections.Generic;
using System.IO;
using Incheol.Controller;
using Incheol.Models.Define;
using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    /// <summary>
    /// 맵 프리팹 안에 배치된 MonsterSpawnPointMarker를 모아 서버가 읽는 SpawnPoints/{mapId}.json
    /// 형식으로 내보낸다. mapId는 프리팹 파일 이름을 그대로 쓴다 - Addressable Address와
    /// AddressableAssetKey enum 이름이 전부 프리팹 파일명과 동일하게 맞춰져 있는(예: Floor001)
    /// 이 프로젝트의 기존 관례를 그대로 따른다.
    /// </summary>
    public static class MonsterSpawnPointExporter
    {
        // Client/와 형제 폴더인 Server/ 프로젝트의 스폰 데이터 폴더로 직접 써서, 내보낸 뒤 수동으로
        // 파일을 옮기는 단계 자체를 없앤다(같은 컴퓨터에서 두 프로젝트를 함께 작업한다는 전제).
        private const string ServerSpawnPointsRelativePath = "../../Server/MainServer/GameServer/SpawnPoints";

        [System.Serializable]
        private class SpawnEntryJson
        {
            public string monsterType;
            public int maxHp;
            public int attackPower;
            public int defense;
            public int expReward;
        }

        [System.Serializable]
        private class SpawnPointJson
        {
            public string pointId;
            public float x;
            public float y;
            public float z;
            public float rotationY;
            public int maxAlive;
            public float respawnSeconds;
            public List<SpawnEntryJson> entries;
            public float detectionRange;
            public float chaseSpeed;
            public float leashRange;
        }

        [System.Serializable]
        private class SpawnPointFileJson
        {
            public List<SpawnPointJson> points = new();
        }

        [MenuItem("Tools/Monster/Export Spawn Points From Selected Prefab")]
        private static void ExportFromSelection()
        {
            GameObject selected = Selection.activeGameObject;
            string assetPath = selected != null ? AssetDatabase.GetAssetPath(selected) : null;

            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab"))
            {
                EditorUtility.DisplayDialog("스폰 포인트 내보내기", "Project 창에서 맵 프리팹(예: Floor001)을 선택한 상태에서 실행하세요.", "확인");
                return;
            }

            ExportPrefabAtPath(assetPath, showDialogs: true);
        }

        // 메뉴 진입점(ExportFromSelection)과 자동화 테스트가 공유하는 실제 내보내기 로직.
        // showDialogs=false면 팝업 없이 조용히 동작한다(테스트/배치 실행용).
        public static int ExportPrefabAtPath(string assetPath, bool showDialogs)
        {
            string mapId = Path.GetFileNameWithoutExtension(assetPath);

            // 프리팹을 씬에 인스턴스화하지 않고 내용만 읽기 위해 임시로 연다.
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                MonsterSpawnPointMarker[] markers = root.GetComponentsInChildren<MonsterSpawnPointMarker>(true);
                if (markers.Length == 0)
                {
                    if (showDialogs)
                    {
                        EditorUtility.DisplayDialog("스폰 포인트 내보내기", $"'{mapId}' 프리팹 안에 MonsterSpawnPointMarker가 없습니다.", "확인");
                    }
                    return 0;
                }

                // 서버가 부팅 시점에 "Entries가 비어있으면 실패"로 막아주긴 하지만, 그 전에 여기서
                // 먼저 걸러야 이 프리팹 하나가 잘못됐다고 다른 포인트까지 포함된 내보내기 전체를
                // 조용히 반쪽짜리로 만들지 않는다 - 문제가 있으면 파일을 아예 안 쓰고 통째로 중단한다.
                foreach (MonsterSpawnPointMarker marker in markers)
                {
                    if (marker.entries == null || marker.entries.Count == 0)
                    {
                        string message = $"'{marker.gameObject.name}'에 몬스터 타입(Entries)이 하나도 없습니다.";
                        if (showDialogs) EditorUtility.DisplayDialog("스폰 포인트 내보내기", message, "확인");
                        Debug.LogError($"[MonsterSpawnPointExporter] {message} 내보내기를 중단합니다.");
                        return 0;
                    }

                    foreach (MonsterSpawnEntry entry in marker.entries)
                    {
                        if (entry.monsterType == MonsterType.None)
                        {
                            string message = $"'{marker.gameObject.name}'의 몬스터 타입 중 선택되지 않은 항목이 있습니다.";
                            if (showDialogs) EditorUtility.DisplayDialog("스폰 포인트 내보내기", message, "확인");
                            Debug.LogError($"[MonsterSpawnPointExporter] {message} 내보내기를 중단합니다.");
                            return 0;
                        }
                    }
                }

                var fileData = new SpawnPointFileJson();
                foreach (MonsterSpawnPointMarker marker in markers)
                {
                    Vector3 position = marker.transform.position;
                    var entries = new List<SpawnEntryJson>();
                    foreach (MonsterSpawnEntry entry in marker.entries)
                    {
                        entries.Add(new SpawnEntryJson
                        {
                            monsterType = entry.monsterType.ToString(),
                            maxHp = entry.maxHp,
                            attackPower = entry.attackPower,
                            defense = entry.defense,
                            expReward = entry.expReward
                        });
                    }

                    fileData.points.Add(new SpawnPointJson
                    {
                        pointId = marker.gameObject.name,
                        x = position.x,
                        y = position.y,
                        z = position.z,
                        rotationY = marker.transform.eulerAngles.y,
                        maxAlive = marker.maxAlive,
                        respawnSeconds = marker.respawnSeconds,
                        entries = entries,
                        detectionRange = marker.detectionRange,
                        chaseSpeed = marker.chaseSpeed,
                        leashRange = marker.leashRange
                    });
                }

                string json = JsonUtility.ToJson(fileData, true);
                string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ServerSpawnPointsRelativePath));
                Directory.CreateDirectory(outputDirectory);
                string outputPath = Path.Combine(outputDirectory, $"{mapId}.json");
                File.WriteAllText(outputPath, json);

                Debug.Log($"[MonsterSpawnPointExporter] '{mapId}' 스폰 포인트 {markers.Length}개를 내보냈습니다 : {outputPath}");
                return markers.Length;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Monster/Export Spawn Points From Selected Prefab", true)]
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
