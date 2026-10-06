using System.Collections.Generic;
using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 보스 스킬의 위험 범위를 바닥에 보여주는 예고 표시 하나. 서버가 예고(Game_BossSkillTelegraphBroadcast)를 보내면 RemoteMonsterManager가
    /// 풀(ObjectPoolManager)에서 하나 대여해 Setup*으로 모양을 정하고, 예고 시간 동안 안쪽이 차오르다가 서버의 종료 알림
    /// (Game_BossSkillEndBroadcast)이 오면 Complete로 발동 섬광(executed) 또는 즉시 소멸(취소)을 재생하고 풀에 반환한다.
    ///
    /// 모양은 세 가지다: 원(범위 공격), 직사각형(돌진 - 시작점에서 앞으로 뻗는다), 작은 원(소환 위치 - 원과 같은 모양).
    /// 별도 텍스처 없이 코드로 만든 평면 메시 세 장(바탕/차오르는 부분/테두리)으로 그리며, 색은 MaterialPropertyBlock으로 바꾼다.
    /// 판정은 서버가 하므로 이 표시는 순수한 연출이다 - 서버의 위험 범위와 같은 크기/위치로 그려야 플레이어가 믿고 피할 수 있다.
    /// </summary>
    public class BossTelegraphIndicator : MonoBehaviour, IPoolable
    {
        private enum Stage
        {
            Inactive,

            // 예고 중: 안쪽이 차오른다.
            Filling,

            // 발동 순간: 짧은 섬광.
            Flashing,

            // 사라지는 중.
            Fading
        }

        [Header("외형")]
        [Tooltip("투명 Unlit 머티리얼(양면, ZWrite 끔). 바탕/차오르는 부분/테두리가 각각 이 머티리얼의 복사본(렌더 큐만 다름)을 쓴다.")]
        [SerializeField] private Material material;

        [Tooltip("위험 범위 바탕색(옅게).")]
        [SerializeField] private Color areaColor = new(1f, 0.12f, 0.08f, 0.25f);

        [Tooltip("예고 시간 동안 차오르는 안쪽 색.")]
        [SerializeField] private Color fillColor = new(1f, 0.2f, 0.1f, 0.5f);

        [Tooltip("범위 테두리 색(진하게 - 경계를 또렷이 보이게 한다).")]
        [SerializeField] private Color edgeColor = new(1f, 0.4f, 0.25f, 0.95f);

        [Tooltip("발동 순간의 섬광 색.")]
        [SerializeField] private Color flashColor = new(1f, 0.95f, 0.8f, 0.9f);

        [Header("크기/위치")]
        [SerializeField, Min(0.01f)] private float edgeThickness = 0.12f;
        [SerializeField, Min(8)] private int circleSegments = 48;

        [Tooltip("바닥과 겹쳐 깜빡이는 것(z-fighting)을 막으려고 지면에서 띄우는 높이.")]
        [SerializeField, Min(0f)] private float groundOffset = 0.05f;

        [Tooltip("지면을 찾을 때 기준점보다 이만큼 위에서 아래로 레이를 쏜다.")]
        [SerializeField, Min(0f)] private float groundProbeHeight = 2f;

        [Tooltip("지면을 찾는 레이의 최대 길이(시작점부터). 이 안에 지면이 없으면 서버가 알려 준 높이를 그대로 쓴다.")]
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 8f;

        [Header("연출 시간")]
        [SerializeField, Min(0f)] private float flashSeconds = 0.12f;
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;
        [SerializeField, Min(0.01f)] private float cancelFadeSeconds = 0.15f;

        [Tooltip("예고 시간이 지나고도 종료 알림이 오지 않을 때(패킷 유실/지연) 이 시간(초)이 더 지나면 스스로 사라진다 - 화면에 영영 남지 않게 한다.")]
        [SerializeField, Min(0f)] private float staleTimeoutSeconds = 2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 서버 위험 범위와 같은 지점에서 지면을 찾을 때 쓰는 레이 버퍼(매번 할당하지 않도록 재사용).
        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        private Renderer areaRenderer;
        private Renderer fillRenderer;
        private Renderer edgeRenderer;
        private Transform fillTransform;
        private Mesh areaMesh;
        private Mesh fillMesh;
        private Mesh edgeMesh;
        private readonly List<Material> instancedMaterials = new();
        private MaterialPropertyBlock propertyBlock;

        private Stage stage = Stage.Inactive;
        private bool isLine;
        private float duration = 1f;
        private float elapsed;
        private float stageTime;
        private float currentFadeSeconds;

        // 섬광 때 키울 기준 크기. Flashing 동안 이 값에서 약간 커진다.
        private Vector3 baseScale = Vector3.one;

        #region LifeCycle
        private bool isBuilt;

        private void Awake()
        {
            EnsureBuilt();
        }

        // 세 겹의 메시 오브젝트를 한 번만 만든다. Awake가 아직 돌지 않은 상태(비활성 인스턴스, 에디터 미리보기)에서 Setup*이 먼저 불려도
        // 안전하도록 Setup*도 이 메서드를 거친다.
        private void EnsureBuilt()
        {
            if (isBuilt)
            {
                return;
            }

            isBuilt = true;
            propertyBlock = new MaterialPropertyBlock();

            // 바탕 -> 차오르는 부분 -> 테두리 순으로 겹쳐 그려야 하므로 렌더 큐를 한 칸씩 다르게 둔 머티리얼 복사본을 쓴다
            // (같은 큐의 투명 메시끼리는 카메라 거리로 정렬돼 겹침 순서가 흔들린다).
            areaRenderer = CreateLayer("Area", 0, out _, out areaMesh);
            fillRenderer = CreateLayer("Fill", 1, out fillTransform, out fillMesh);
            edgeRenderer = CreateLayer("Edge", 2, out _, out edgeMesh);

            SetRenderersEnabled(false);
        }

        private void OnDestroy()
        {
            foreach (Material instanced in instancedMaterials)
            {
                if (instanced != null)
                {
                    Destroy(instanced);
                }
            }

            DestroyMesh(areaMesh);
            DestroyMesh(fillMesh);
            DestroyMesh(edgeMesh);
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private Renderer CreateLayer(string layerName, int renderQueueOffset, out Transform layerTransform, out Mesh mesh)
        {
            var go = new GameObject(layerName);
            go.transform.SetParent(transform, false);
            layerTransform = go.transform;

            mesh = new Mesh { name = $"BossTelegraph_{layerName}" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material == null)
            {
                DebugLogManager.GenerateErrorMessage<BossTelegraphIndicator>("머티리얼이 지정되지 않았습니다 - 위험 범위가 보이지 않습니다.");
            }
            else
            {
                var instanced = new Material(material) { renderQueue = material.renderQueue + renderQueueOffset };
                instancedMaterials.Add(instanced);
                meshRenderer.sharedMaterial = instanced;
            }

            return meshRenderer;
        }
        #endregion

        #region Pooling
        public void OnGetFromPool()
        {
            // Setup*이 호출되기 전에는(콜백이 다음 프레임에 올 수 있다) 지난 모양이 잠깐 보이지 않도록 숨긴다.
            stage = Stage.Inactive;
            SetRenderersEnabled(false);
        }

        public void OnReleaseToPool()
        {
            stage = Stage.Inactive;
            SetRenderersEnabled(false);
        }
        #endregion

        #region Setup
        /// <summary>
        /// 원형 위험 범위(범위 공격). center를 중심으로 반지름 radius인 원이 durationSeconds 동안 차오른다.
        /// </summary>
        public void SetupCircle(Vector3 center, float radius, float durationSeconds)
        {
            Begin(center, 0f, durationSeconds, isLineShape: false);

            BuildDisc(areaMesh, radius, circleSegments);
            BuildDisc(fillMesh, radius, circleSegments);
            BuildRing(edgeMesh, radius, Mathf.Min(edgeThickness, radius * 0.5f), circleSegments);
        }

        /// <summary>
        /// 소환 위치처럼 작은 원 표시. 모양은 원과 같다(반지름만 작다).
        /// </summary>
        public void SetupMarker(Vector3 center, float radius, float durationSeconds)
        {
            SetupCircle(center, radius, durationSeconds);
        }

        /// <summary>
        /// 직사각형 위험 범위(돌진). start에서 rotationY(도, 몬스터 RotationY와 같은 규칙) 방향으로 길이 length, 폭 width만큼 뻗고,
        /// 안쪽은 시작점에서부터 앞으로 차오른다.
        /// </summary>
        public void SetupLine(Vector3 start, float rotationY, float width, float length, float durationSeconds)
        {
            Begin(start, rotationY, durationSeconds, isLineShape: true);

            BuildRect(areaMesh, width, length);
            BuildRect(fillMesh, width, length);
            BuildFrame(edgeMesh, width, length, Mathf.Min(edgeThickness, width * 0.5f));
        }

        /// <summary>
        /// 차오르는 정도(0~1)를 직접 지정해 한 번 그린다. 평소에는 Update가 시간 경과로 이 값을 올리므로 쓸 일이 없고,
        /// 에디터 미리보기/테스트에서 특정 진행 상태를 보고 싶을 때 쓴다.
        /// </summary>
        public void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            elapsed = progress * duration;
            ApplyProgress(progress);
        }

        private void Begin(Vector3 position, float rotationY, float durationSeconds, bool isLineShape)
        {
            EnsureBuilt();
            isLine = isLineShape;
            duration = Mathf.Max(0.05f, durationSeconds);
            elapsed = 0f;
            stageTime = 0f;
            baseScale = Vector3.one;

            transform.SetPositionAndRotation(SnapToGround(position), Quaternion.Euler(0f, rotationY, 0f));
            transform.localScale = baseScale;

            // 차오르는 부분은 0에서 시작한다. 바탕/테두리 위에 보이도록 아주 조금씩 위로 띄운다.
            fillTransform.localScale = isLine ? new Vector3(1f, 1f, 0f) : new Vector3(0f, 1f, 0f);
            fillTransform.localPosition = new Vector3(0f, 0.01f, 0f);

            ApplyColors(1f, 1f);
            SetRenderersEnabled(true);
            stage = Stage.Filling;
        }

        // 서버 좌표의 높이(Y)를 발밑 지면 높이로 바꾼다. 서버는 지형 정보가 없어 몬스터 높이(스폰 포인트 높이)만 알려 주므로,
        // 몬스터와 같은 방식(RemoteMonsterController.SnapToGround)으로 지면을 찾아 그 위에 살짝 띄워 깐다.
        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * groundProbeHeight;
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, groundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = GroundHits[i];

                // 플레이어/몬스터(Rigidbody 보유)와 자기 자신은 지면이 아니다.
                if (hit.rigidbody != null || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    position.y = hit.point.y;
                }
            }

            position.y += groundOffset;
            return position;
        }
        #endregion

        #region Update
        private void Update()
        {
            switch (stage)
            {
                case Stage.Filling:
                    UpdateFilling();
                    break;

                case Stage.Flashing:
                    UpdateFlashing();
                    break;

                case Stage.Fading:
                    UpdateFading();
                    break;
            }
        }

        private void UpdateFilling()
        {
            elapsed += Time.deltaTime;
            ApplyProgress(Mathf.Clamp01(elapsed / duration));

            // 종료 알림이 오지 않고 예고 시간이 한참 지났다면 스스로 정리한다.
            if (elapsed > duration + staleTimeoutSeconds)
            {
                BeginFade(cancelFadeSeconds);
            }
        }

        // 차오르는 부분의 크기와 테두리 깜빡임을 진행도에 맞춰 칠한다.
        private void ApplyProgress(float progress)
        {
            fillTransform.localScale = isLine ? new Vector3(1f, 1f, progress) : new Vector3(progress, 1f, progress);

            // 가득 찬 뒤(판정 직전)에는 테두리가 더 빠르게 깜빡여 "곧 터진다"는 신호를 준다.
            float pulse = progress >= 1f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 28f) : 0.8f + 0.2f * Mathf.Sin(Time.time * 12f);
            ApplyColors(1f, pulse);
        }

        private void UpdateFlashing()
        {
            stageTime += Time.deltaTime;
            float t = Mathf.Clamp01(stageTime / Mathf.Max(0.0001f, flashSeconds));

            // 섬광 동안 살짝 커져 "터지는" 느낌을 준다.
            transform.localScale = baseScale * (1f + 0.06f * t);

            if (t >= 1f)
            {
                BeginFade(fadeSeconds);
            }
        }

        private void UpdateFading()
        {
            stageTime += Time.deltaTime;
            float t = Mathf.Clamp01(stageTime / Mathf.Max(0.0001f, currentFadeSeconds));
            ApplyColors(1f - t, 1f);

            if (t >= 1f)
            {
                ReturnToPool();
            }
        }
        #endregion

        #region Complete
        /// <summary>
        /// 서버의 종료 알림을 반영한다. executed=true면 발동 섬광 후 사라지고, false(취소)면 바로 사라진다.
        /// 이미 사라지는 중이거나 쓰이지 않는 표시에는 아무 일도 하지 않는다.
        /// </summary>
        public void Complete(bool executed)
        {
            if (stage == Stage.Inactive || stage == Stage.Fading)
            {
                return;
            }

            if (!executed)
            {
                BeginFade(cancelFadeSeconds);
                return;
            }

            // 발동: 차오르던 부분을 가득 채우고 섬광 색으로 바꾼다.
            stage = Stage.Flashing;
            stageTime = 0f;
            fillTransform.localScale = Vector3.one;
            ApplyFlashColors();
        }

        private void BeginFade(float seconds)
        {
            stage = Stage.Fading;
            stageTime = 0f;
            currentFadeSeconds = seconds;
        }

        private void ReturnToPool()
        {
            stage = Stage.Inactive;

            if (ObjectPoolManager.Instance != null)
            {
                ObjectPoolManager.Instance.Release(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
        #endregion

        #region Rendering
        private void SetRenderersEnabled(bool enabled)
        {
            if (areaRenderer != null) areaRenderer.enabled = enabled;
            if (fillRenderer != null) fillRenderer.enabled = enabled;
            if (edgeRenderer != null) edgeRenderer.enabled = enabled;
        }

        // 알파 배율(페이드)과 테두리 깜빡임(edgePulse)을 반영해 세 겹의 색을 칠한다.
        private void ApplyColors(float alphaMultiplier, float edgePulse)
        {
            Paint(areaRenderer, areaColor, alphaMultiplier);
            Paint(fillRenderer, fillColor, alphaMultiplier);
            Paint(edgeRenderer, edgeColor, alphaMultiplier * edgePulse);
        }

        private void ApplyFlashColors()
        {
            Paint(areaRenderer, flashColor, 0.8f);
            Paint(fillRenderer, flashColor, 1f);
            Paint(edgeRenderer, flashColor, 1f);
        }

        private void Paint(Renderer target, Color color, float alphaMultiplier)
        {
            if (target == null)
            {
                return;
            }

            color.a *= alphaMultiplier;
            propertyBlock.SetColor(BaseColorId, color);
            target.SetPropertyBlock(propertyBlock);
        }
        #endregion

        #region Mesh Builders
        // 모든 메시는 y=0 평면 위에 그린다(머티리얼이 양면이라 감는 방향은 상관없다).

        // 중심에서 부채꼴로 펼친 원판.
        private static void BuildDisc(Mesh mesh, float radius, int segments)
        {
            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            Assign(mesh, vertices, triangles);
        }

        // 바깥 반지름 outerRadius, 두께 thickness인 고리.
        private static void BuildRing(Mesh mesh, float outerRadius, float thickness, int segments)
        {
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                vertices[i * 2 + 1] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);

                int next = (i + 1) % segments;
                int index = i * 6;
                triangles[index] = i * 2;
                triangles[index + 1] = next * 2;
                triangles[index + 2] = i * 2 + 1;
                triangles[index + 3] = i * 2 + 1;
                triangles[index + 4] = next * 2;
                triangles[index + 5] = next * 2 + 1;
            }

            Assign(mesh, vertices, triangles);
        }

        // 시작선(z=0)에서 앞(+z)으로 length만큼 뻗는 폭 width의 직사각형. 차오르는 부분을 z 스케일로 키울 때 시작선이 고정된다.
        private static void BuildRect(Mesh mesh, float width, float length)
        {
            float half = width / 2f;
            Assign(mesh,
                new[] { new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), new Vector3(half, 0f, length), new Vector3(-half, 0f, length) },
                new[] { 0, 3, 1, 1, 3, 2 });
        }

        // BuildRect와 같은 크기의 직사각형 테두리(두께 thickness의 사각 고리).
        private static void BuildFrame(Mesh mesh, float width, float length, float thickness)
        {
            float outerHalf = width / 2f;
            float innerHalf = Mathf.Max(0f, outerHalf - thickness);
            float innerStart = Mathf.Min(thickness, length / 2f);
            float innerEnd = Mathf.Max(innerStart, length - thickness);

            var vertices = new[]
            {
                new Vector3(-outerHalf, 0f, 0f), new Vector3(outerHalf, 0f, 0f), new Vector3(outerHalf, 0f, length), new Vector3(-outerHalf, 0f, length),
                new Vector3(-innerHalf, 0f, innerStart), new Vector3(innerHalf, 0f, innerStart), new Vector3(innerHalf, 0f, innerEnd), new Vector3(-innerHalf, 0f, innerEnd)
            };

            // 바깥 4점(0~3)과 안쪽 4점(4~7)을 이어 변마다 사다리꼴 두 장.
            var triangles = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                triangles.AddRange(new[] { i, next, i + 4, i + 4, next, next + 4 });
            }

            Assign(mesh, vertices, triangles.ToArray());
        }

        private static void Assign(Mesh mesh, Vector3[] vertices, int[] triangles)
        {
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
        #endregion
    }
}
