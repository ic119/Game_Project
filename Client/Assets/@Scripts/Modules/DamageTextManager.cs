using System.Collections.Generic;
using Incheol.Controller;
using Incheol.Modules.Networking;
using Incheol.Utils;
using TMPro;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// 전투 중 피해량/회피를 대상 머리 위에 떠오르는 숫자로 보여준다. 서버가 보낸 전투 알림(Game_MonsterDamageBroadcast,
    /// Game_MonsterAttackBroadcast, Game_DamageBroadcast, Game_MonsterAttackDodgedBroadcast)을 구독해 그린다 - 피해량은 서버가
    /// 이미 방어력까지 계산한 최종값이라 그대로 표시한다. 텍스트는 월드 공간 TextMeshPro(빌보드)이고 한 번 만든 오브젝트를
    /// 풀에 돌려놓고 재사용해 전투 중 할당/GC를 만들지 않는다.
    /// </summary>
    public class DamageTextManager : SingletonObject<DamageTextManager>
    {
        #region Variable
        // 스타일: (색, 크기 배율). 내가 한 일/나에게 일어난 일은 크고 진하게, 남의 일은 작고 옅게 보여 내 전투가 한눈에 읽히게 한다.
        private static readonly Color MyHitColor = new Color(1f, 0.92f, 0.4f);
        private static readonly Color OtherHitColor = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color TakenDamageColor = new Color(1f, 0.3f, 0.3f);
        private static readonly Color OtherTakenDamageColor = new Color(1f, 0.55f, 0.35f);
        private static readonly Color DodgeColor = new Color(0.45f, 0.9f, 1f);

        private const float MyHitScale = 1.25f;
        private const float OtherHitScale = 0.8f;
        private const float TakenDamageScale = 1.25f;
        private const float OtherTakenDamageScale = 0.85f;
        private const float DodgeScale = 1.1f;

        private const float BaseFontSize = 4f;
        private const float Duration = 0.9f;
        private const float RiseHeight = 1.1f;
        private const float PopDuration = 0.12f;
        private const float PopScale = 1.5f;
        private const float HorizontalJitter = 0.3f;
        private const int MaxActive = 40;

        // 대상 머리 위로 올릴 기본 높이. 몬스터는 콜라이더 위쪽을 기준으로 하고, 못 찾으면 이 값을 쓴다.
        private const float DefaultHeadHeight = 2f;
        private const float HeadMargin = 0.3f;

        private sealed class FloatingText
        {
            public TextMeshPro Text;
            public float StartTime;
            public Vector3 StartPosition;
            public float Scale;
            public Color Color;
        }

        private readonly List<FloatingText> active = new();
        private readonly Stack<FloatingText> pool = new();

        private TMP_FontAsset font;
        private Material overlayMaterial;
        private Camera mainCamera;
        private PlayerMoveController localPlayer;
        #endregion

        #region LifeCycle
        protected override void Awake()
        {
            base.Awake();

            font = Resources.Load<TMP_FontAsset>("Fonts/Maplestory Light SDF");
            if (font == null)
            {
                font = TMP_Settings.defaultFontAsset;
            }
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnMonsterDamaged += HandleMonsterDamaged;
            GameServerConnectManager.Instance.OnMonsterAttacked += HandleMonsterAttacked;
            GameServerConnectManager.Instance.OnDamageReceived += HandlePlayerDamaged;
            GameServerConnectManager.Instance.OnMonsterAttackDodged += HandleMonsterAttackDodged;
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnMonsterDamaged -= HandleMonsterDamaged;
            GameServerConnectManager.Instance.OnMonsterAttacked -= HandleMonsterAttacked;
            GameServerConnectManager.Instance.OnDamageReceived -= HandlePlayerDamaged;
            GameServerConnectManager.Instance.OnMonsterAttackDodged -= HandleMonsterAttackDodged;
        }

        protected override void OnDestroy()
        {
            if (overlayMaterial != null)
            {
                Destroy(overlayMaterial);
            }

            base.OnDestroy();
        }

        private void LateUpdate()
        {
            if (active.Count == 0)
            {
                return;
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                FloatingText entry = active[i];
                float elapsed = Time.time - entry.StartTime;

                if (elapsed >= Duration)
                {
                    Release(entry);
                    active.RemoveAt(i);
                    continue;
                }

                float t = elapsed / Duration;

                // 위로 올라가되 끝으로 갈수록 느려진다(ease-out).
                float rise = RiseHeight * (1f - (1f - t) * (1f - t));
                entry.Text.transform.position = entry.StartPosition + Vector3.up * rise;

                // 처음 짧게 커졌다가 원래 크기로 돌아온다(pop).
                float pop = elapsed < PopDuration ? Mathf.Lerp(PopScale, 1f, elapsed / PopDuration) : 1f;
                entry.Text.transform.localScale = Vector3.one * (entry.Scale * pop);

                // 마지막 40%에서 서서히 사라진다.
                float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                Color color = entry.Color;
                color.a = alpha;
                entry.Text.color = color;

                if (mainCamera != null)
                {
                    entry.Text.transform.rotation = mainCamera.transform.rotation;
                }
            }
        }
        #endregion

        #region Method
        /// <summary>몬스터가 맞았다(누가 때렸든 시야 안이면 온다). 내가 때린 건 크게, 남이 때린 건 작게.</summary>
        private void HandleMonsterDamaged(GameMonsterDamageBroadcastPacket packet)
        {
            if (RemoteMonsterManager.Instance == null || !RemoteMonsterManager.Instance.TryGetRemoteMonster(packet.MonsterId, out RemoteMonsterController monster))
            {
                return;
            }

            bool isMine = IsLocalPlayer(packet.AttackerId);
            Spawn(packet.Damage.ToString(), GetMonsterHeadPosition(monster), isMine ? MyHitColor : OtherHitColor, isMine ? MyHitScale : OtherHitScale);
        }

        /// <summary>플레이어가 몬스터에게 맞았다. 내가 맞은 건 크게 빨갛게, 다른 플레이어는 작게.</summary>
        private void HandleMonsterAttacked(GameMonsterAttackBroadcastPacket packet)
        {
            SpawnPlayerTaken(packet.TargetPlayerId, packet.Damage.ToString());
        }

        /// <summary>PvP로 플레이어가 맞았다.</summary>
        private void HandlePlayerDamaged(GameDamageBroadcastPacket packet)
        {
            SpawnPlayerTaken(packet.TargetId, packet.Damage.ToString());
        }

        /// <summary>몬스터 공격이 대쉬 회피에 막혔다(HP는 변하지 않는다).</summary>
        private void HandleMonsterAttackDodged(GameMonsterAttackDodgedBroadcastPacket packet)
        {
            if (TryGetPlayerHeadPosition(packet.TargetPlayerId, out Vector3 position))
            {
                Spawn("회피", position, DodgeColor, DodgeScale);
            }
        }

        private void SpawnPlayerTaken(long playerId, string text)
        {
            if (!TryGetPlayerHeadPosition(playerId, out Vector3 position))
            {
                return;
            }

            bool isMine = IsLocalPlayer(playerId);
            Spawn(text, position, isMine ? TakenDamageColor : OtherTakenDamageColor, isMine ? TakenDamageScale : OtherTakenDamageScale);
        }

        private void Spawn(string text, Vector3 headPosition, Color color, float scale)
        {
            if (font == null)
            {
                return;
            }

            // 한꺼번에 너무 많이 쌓이면(광역 전투) 가장 오래된 것부터 걷는다.
            if (active.Count >= MaxActive)
            {
                Release(active[0]);
                active.RemoveAt(0);
            }

            FloatingText entry = pool.Count > 0 ? pool.Pop() : Create();
            entry.StartTime = Time.time;
            entry.Color = color;
            entry.Scale = scale;

            // 같은 대상에게 연속으로 뜨는 숫자가 겹치지 않도록 좌우로 조금 흩뿌린다.
            Vector3 jitter = new Vector3(Random.Range(-HorizontalJitter, HorizontalJitter), 0f, Random.Range(-HorizontalJitter, HorizontalJitter));
            entry.StartPosition = headPosition + jitter;

            entry.Text.text = text;
            entry.Text.color = color;
            entry.Text.transform.position = entry.StartPosition;
            entry.Text.transform.localScale = Vector3.one * (scale * PopScale);
            entry.Text.gameObject.SetActive(true);

            active.Add(entry);
        }

        private FloatingText Create()
        {
            GameObject go = new GameObject("DamageText");
            go.transform.SetParent(transform, false);

            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = BaseFontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            // 벽/캐릭터 뒤에 가려지지 않도록 깊이 검사를 끈 재질을 모든 텍스트가 공유한다(숫자는 항상 보여야 한다).
            if (overlayMaterial == null)
            {
                overlayMaterial = new Material(font.material);
                overlayMaterial.SetFloat(ShaderUtilities.ShaderTag_ZTestMode, (float)UnityEngine.Rendering.CompareFunction.Always);
                overlayMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
                overlayMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            }

            text.fontSharedMaterial = overlayMaterial;

            // 반투명 UI/이펙트 위에도 그려지도록 정렬 순서를 높인다.
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 100;
            }

            go.SetActive(false);
            return new FloatingText { Text = text };
        }

        private void Release(FloatingText entry)
        {
            entry.Text.gameObject.SetActive(false);
            pool.Push(entry);
        }

        private static bool IsLocalPlayer(long playerId)
        {
            return SaveDataManager.Instance != null
                && SaveDataManager.Instance.SelectedCharacterId.HasValue
                && SaveDataManager.Instance.SelectedCharacterId.Value == playerId;
        }

        private bool TryGetPlayerHeadPosition(long playerId, out Vector3 position)
        {
            position = default;

            Transform target = null;
            if (IsLocalPlayer(playerId))
            {
                if (localPlayer == null)
                {
                    localPlayer = FindAnyObjectByType<PlayerMoveController>();
                }

                target = localPlayer != null ? localPlayer.transform : null;
            }
            else if (RemotePlayerManager.Instance != null && RemotePlayerManager.Instance.TryGetRemotePlayer(playerId, out RemoteCharacterController remote))
            {
                target = remote.transform;
            }

            if (target == null)
            {
                return false;
            }

            position = target.position + Vector3.up * DefaultHeadHeight;
            return true;
        }

        private static Vector3 GetMonsterHeadPosition(RemoteMonsterController monster)
        {
            Collider collider = monster.GetComponentInChildren<Collider>();
            if (collider != null && collider.enabled)
            {
                Vector3 position = monster.transform.position;
                position.y = collider.bounds.max.y + HeadMargin;
                return position;
            }

            return monster.transform.position + Vector3.up * DefaultHeadHeight;
        }
        #endregion
    }
}
