using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Incheol.Editor
{
    /// <summary>
    /// 맵 프리팹의 콜라이더(벽/기둥/가구)를 조사해, 몬스터가 걸어갈 수 있는 칸을 서버가 읽는 NavGrids/{mapId}.json으로 내보낸다.
    /// 서버 몬스터 이동에는 물리 엔진이 없어서 이 격자가 가구와 벽을 통과하지 못하게 하는 유일한 근거다(서버 GameServer.Navigation.NavGrid).
    /// MonsterSpawnPointExporter가 활동 영역(confineToArea)이 있는 마커를 발견하면 스폰 포인트 내보내기와 함께 호출한다.
    ///
    /// 조사 방식: 활동 영역(방) 안의 칸마다 (1) 발밑에 바닥이 있는지(위에서 아래로 레이), (2) 허리 높이 구간[BandBottom, BandTop]에
    /// 콜라이더가 겹치는지(박스 겹침)를 격리된 프리뷰 씬의 물리 쿼리로 확인한다. 바닥 높이는 영역을 정의한 마커의 Y를 기준으로 한다 -
    /// 방마다 바닥 높이가 다르고(던전은 -8m~+6m까지 다양하다) 바닥 위 가구 윗면을 바닥으로 오인하지 않으려면, 마커를 방 바닥에 두는 것이
    /// 전제다. 어느 영역에도 속하지 않은 칸은 모두 이동 불가다.
    /// </summary>
    public static class MonsterNavGridExporter
    {
        // 서버 NavGrid의 칸 크기. 작을수록 정밀하지만 칸 수가 제곱으로 늘어난다.
        public const float CellSize = 0.5f;

        // 바닥에서 이 높이보다 낮은 물체(문턱, 낮은 계단, 바닥 장식)는 걸어 넘을 수 있어 장애물로 보지 않는다. 너무 높이면 낮은 가구
        // (벤치 윗면이 바닥 위 약 0.49m)를 몬스터가 통과하고, 너무 낮추면 문턱과 울퉁불퉁한 바닥이 장애물이 된다.
        private const float BandBottom = 0.35f;

        // 몬스터 키 높이에 해당하는 구간의 위쪽 끝. 이 위(천장 장식, 높은 조명)는 지나다니는 데 방해가 되지 않는다.
        private const float BandTop = 1.7f;

        // 바닥 확인 레이를 영역 기준 높이보다 이만큼 위에서 쏘고, GroundProbeDistance만큼 내려가며 찾는다.
        private const float GroundProbeUp = 0.5f;
        private const float GroundProbeDistance = 2f;

        // 바닥으로 인정하는 면의 최소 기울기(법선 Y). 이보다 가파르면 벽으로 본다.
        private const float MinGroundNormalY = 0.5f;

        // Client/와 형제 폴더인 Server/ 프로젝트의 이동 격자 폴더로 직접 써서, 내보낸 뒤 수동으로 옮기는 단계를 없앤다.
        private const string ServerNavGridsRelativePath = "../../Server/MainServer/GameServer/NavGrids";

        /// <summary>
        /// 이동 가능 영역 하나(방). floorY는 이 영역의 바닥 높이(영역을 정의한 마커의 Y)다.
        /// </summary>
        public readonly struct AreaInput
        {
            public readonly string name;
            public readonly Vector2 center;
            public readonly Vector2 size;
            public readonly float floorY;

            public AreaInput(string name, Vector2 center, Vector2 size, float floorY)
            {
                this.name = name;
                this.center = center;
                this.size = size;
                this.floorY = floorY;
            }

            public bool Contains(float x, float z)
            {
                return Mathf.Abs(x - center.x) <= size.x / 2f && Mathf.Abs(z - center.y) <= size.y / 2f;
            }
        }

        public class BakeResult
        {
            public float originX;
            public float originZ;
            public List<string> rows = new();
            public int walkableCount;
            public int totalCount;
        }

        // JsonUtility용 파일 형식. 서버 NavGridCatalog.NavGridFile과 같아야 한다.
        [System.Serializable]
        private class NavGridFileJson
        {
            public float cellSize;
            public float originX;
            public float originZ;
            public List<string> rows;
        }

        /// <summary>
        /// 프리팹을 격리된 프리뷰 씬에 올려 areas 안의 칸을 조사한다. 사용자가 열어 둔 씬에는 영향을 주지 않는다.
        /// </summary>
        public static BakeResult Bake(GameObject prefabAsset, IReadOnlyList<AreaInput> areas)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (AreaInput area in areas)
            {
                minX = Mathf.Min(minX, area.center.x - area.size.x / 2f);
                maxX = Mathf.Max(maxX, area.center.x + area.size.x / 2f);
                minZ = Mathf.Min(minZ, area.center.y - area.size.y / 2f);
                maxZ = Mathf.Max(maxZ, area.center.y + area.size.y / 2f);
            }

            // 원점을 칸 크기의 배수에 맞춰 두면 영역 경계와 칸 경계가 어긋나는 정도가 일정하다.
            float originX = Mathf.Floor(minX / CellSize) * CellSize;
            float originZ = Mathf.Floor(minZ / CellSize) * CellSize;
            int width = Mathf.CeilToInt((maxX - originX) / CellSize);
            int height = Mathf.CeilToInt((maxZ - originZ) / CellSize);

            var result = new BakeResult { originX = originX, originZ = originZ, totalCount = width * height };

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                PrefabUtility.InstantiatePrefab(prefabAsset, scene);
                Physics.SyncTransforms();
                PhysicsScene physics = scene.GetPhysicsScene();

                var overlapBuffer = new Collider[1];
                var rowBuilder = new System.Text.StringBuilder(width);

                for (int cz = 0; cz < height; cz++)
                {
                    rowBuilder.Clear();
                    for (int cx = 0; cx < width; cx++)
                    {
                        float x = originX + (cx + 0.5f) * CellSize;
                        float z = originZ + (cz + 0.5f) * CellSize;

                        bool walkable = TryFindArea(areas, x, z, out AreaInput area) && IsCellWalkable(physics, overlapBuffer, x, z, area.floorY);
                        rowBuilder.Append(walkable ? '.' : '#');
                        if (walkable)
                        {
                            result.walkableCount++;
                        }
                    }

                    result.rows.Add(rowBuilder.ToString());
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            return result;
        }

        private static bool TryFindArea(IReadOnlyList<AreaInput> areas, float x, float z, out AreaInput found)
        {
            // 영역이 겹치면 먼저 정의된 것을 쓴다.
            foreach (AreaInput area in areas)
            {
                if (area.Contains(x, z))
                {
                    found = area;
                    return true;
                }
            }

            found = default;
            return false;
        }

        private static bool IsCellWalkable(PhysicsScene physics, Collider[] overlapBuffer, float x, float z, float floorY)
        {
            // (1) 발밑에 바닥이 있어야 한다 - 구멍이나 낭떠러지는 이동 불가.
            var origin = new Vector3(x, floorY + GroundProbeUp, z);
            if (!physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || hit.normal.y < MinGroundNormalY)
            {
                return false;
            }

            // (2) 허리 높이 구간에 콜라이더가 겹치지 않아야 한다 - 벽, 기둥, 가구. 박스를 칸보다 아주 조금 작게 해서
            // 옆 칸의 벽이 경계에 닿기만 해도 이 칸이 막히는 것을 피한다.
            float bandCenterY = floorY + (BandBottom + BandTop) / 2f;
            var halfExtents = new Vector3(CellSize / 2f - 0.01f, (BandTop - BandBottom) / 2f, CellSize / 2f - 0.01f);
            int overlaps = physics.OverlapBox(new Vector3(x, bandCenterY, z), halfExtents, overlapBuffer, Quaternion.identity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return overlaps == 0;
        }

        /// <summary>
        /// 조사한 격자를 서버 NavGrids/{mapId}.json으로 쓰고 파일 경로를 돌려준다.
        /// </summary>
        public static string Export(string mapId, GameObject prefabAsset, IReadOnlyList<AreaInput> areas)
        {
            BakeResult bake = Bake(prefabAsset, areas);

            var file = new NavGridFileJson
            {
                cellSize = CellSize,
                originX = bake.originX,
                originZ = bake.originZ,
                rows = bake.rows
            };

            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ServerNavGridsRelativePath));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, $"{mapId}.json");
            File.WriteAllText(outputPath, JsonUtility.ToJson(file, true));

            Debug.Log($"[MonsterNavGridExporter] '{mapId}' 이동 격자를 내보냈습니다 : 이동 가능 {bake.walkableCount}/{bake.totalCount}칸, {outputPath}");
            return outputPath;
        }
    }
}
