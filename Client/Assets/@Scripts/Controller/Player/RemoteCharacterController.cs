using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 다른 플레이어의 캐릭터를 표현한다. 입력을 직접 받는 PlayerMoveController와 달리, GameServerConnectManager가 수신한
    /// Game_WorldSnapshot 위치를 서버 시각과 함께 쌓아 두고(SnapshotInterpolationBuffer), 서버 시각 기준 조금 과거
    /// (InterpolationDelayMs) 위치를 앞뒤 두 스냅샷 사이에서 보간해 그린다 - 패킷 도착 간격이 흔들려도 일정한 속도로 움직인다.
    /// </summary>
    public class RemoteCharacterController : MonoBehaviour
    {
        [Tooltip("보간된 이동 속도(m/s)가 이 값을 넘으면 이동 애니메이션(IsMove)을 재생한다.")]
        [SerializeField, Min(0f)] private float moveSpeedThreshold = 0.2f;

        [Tooltip("멈춘 뒤 이 시간(초)이 지나야 정지 애니메이션(IsIdle)으로 바꾼다 - 스냅샷 사이 짧은 정지로 애니메이션이 깜빡이지 않게 한다.")]
        [SerializeField, Min(0f)] private float idleDelay = 0.15f;

        [Header("Dash Effect")]
        [Tooltip("대쉬 이펙트(Dash01)를 재생할 위치. 캐릭터 기준 로컬 오프셋이며 PlayerMoveController.dashEffectPositionOffset과 같은 값을 쓴다.")]
        [SerializeField] private Vector3 dashEffectPositionOffset = Vector3.zero;
        [Tooltip("Dash01 이펙트 프리팹 크기에 곱해지는 배율. PlayerMoveController.dashEffectScale과 같은 값을 쓴다.")]
        [SerializeField, Min(0.01f)] private float dashEffectScale = 0.45f;

        // Attack Layer에 Attack1/Attack2 두 단계만 있다(PlayerAttackController.maxComboStage와 같은 값이어야 한다).
        private const int MaxComboStage = 2;
        private const string AttackLayerName = "Attack Layer";

        private readonly SnapshotInterpolationBuffer interpolation = new();
        private Animator animator;
        private PlayerCharacterModel playerCharacterModel;
        private float lastMovingTime = float.NegativeInfinity;
        private int attackLayerIndex = -1;
        private float attackAnimationEndTime = float.NegativeInfinity;

        // 대쉬 모션 구간(Time.time 기준). 알림은 서버 시각 기준 "지금" 도착하지만 화면의 위치는 InterpolationDelayMs만큼 과거라,
        // 모션도 그만큼 늦춰 시작해야 위치 이동(대쉬 구간)과 맞는다.
        private float dashStartTime = float.PositiveInfinity;
        private float dashEndTime = float.NegativeInfinity;
        private bool isBackDash;
        private bool dashEffectPlayed;

        /// <summary>
        /// 이 원격 캐릭터가 나타내는 서버측 플레이어 id. RemotePlayerManager가 스폰 직후 SetPlayerId로 채운다.
        /// 공격 대상 판정(PlayerAttackController)에서 콜라이더로부터 대상의 id를 즉시 얻는 데 쓴다.
        /// </summary>
        public long PlayerId { get; private set; }

        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
        private static readonly int IsDashHash = Animator.StringToHash("IsDash");
        private static readonly int IsBackDashHash = Animator.StringToHash("IsBackDash");
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");

        private void Awake()
        {
            interpolation.Reset(transform.position, transform.eulerAngles.y);
            animator = GetComponent<Animator>();
            playerCharacterModel = GetComponent<PlayerCharacterModel>();
            if (animator != null)
            {
                attackLayerIndex = animator.GetLayerIndex(AttackLayerName);
            }
        }

        private void Update()
        {
            interpolation.Sample(ServerClock.NowMs - SnapshotInterpolationBuffer.InterpolationDelayMs, out Vector3 position, out float rotationY);

            float speed = Time.deltaTime > 0f ? Vector3.Distance(transform.position, position) / Time.deltaTime : 0f;
            if (speed > moveSpeedThreshold)
            {
                lastMovingTime = Time.time;
            }

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));

            if (animator != null)
            {
                bool isMoving = Time.time - lastMovingTime < idleDelay;
                animator.SetBool(IsMoveHash, isMoving);
                animator.SetBool(IsIdleHash, !isMoving);
            }

            UpdateDash();

            // 공격 모션 재생 시간이 끝나면 Attack Layer 가중치를 다시 0으로 내린다(PlayerAttackController.ResetCombo와 동일한 목적).
            // 피격 모션(PlayerCharacterModel)이 재생 중일 때는 건드리지 않는다 - 피격 모션도 Attack Layer를 쓰므로, 여기서 가중치를
            // 0으로 되돌리면 다른 플레이어 화면에서는 피격 모션이 보이지 않는다. 가중치는 피격 모션 쪽이 직접 내린다.
            bool hitReactionActive = playerCharacterModel != null && playerCharacterModel.IsHitReactionPlaying;

            if (attackLayerIndex >= 0 && animator != null && !hitReactionActive && animator.GetLayerWeight(attackLayerIndex) > 0f
                && Time.time >= attackAnimationEndTime)
            {
                animator.SetInteger(ComboIndexHash, 0);
                animator.SetLayerWeight(attackLayerIndex, 0f);
            }
        }

        /// <summary>
        /// Game_AttackAnimationBroadcast 수신 시(PlayerAttackController.HandleAttackAnimationReceived가 중계) 호출된다.
        /// weaponType은 공격자가 실제로 장착한 무기 그대로라 콤보 모션/타이밍이 정확하다. 이 캐릭터가 실제로 들고 있는
        /// 무기/갑옷/투구의 "시각"(메시)은 서버가 알려주는 장착 정보(GamePlayerInfo, Game_EquipmentChangedBroadcast)를
        /// RemotePlayerManager가 반영한다.
        /// </summary>
        public void PlayAttackAnimation(int comboStage, WeaponType weaponType)
        {
            if (animator == null || attackLayerIndex < 0)
            {
                return;
            }

            // 사망 패킷 뒤에 늦게 도착한 공격 모션 알림이 Attack Layer를 다시 올려 Die를 덮지 않게 한다.
            if (playerCharacterModel != null && playerCharacterModel.IsDead)
            {
                return;
            }

            // 피격 모션을 재생 중이었다면 끝내고 공격 모션으로 넘어간다(ComboIndex를 올리기 전에 불러야 한다).
            if (playerCharacterModel != null)
            {
                playerCharacterModel.CancelHitReaction();
            }

            animator.SetFloat(WeaponIndexHash, (float)weaponType);
            animator.SetInteger(ComboIndexHash, comboStage);
            animator.SetLayerWeight(attackLayerIndex, 1f);
            attackAnimationEndTime = Time.time + GetStageDuration(weaponType, comboStage);
        }

        /// <summary>
        /// Game_DashBroadcast 수신 시(RemotePlayerManager가 중계) 호출된다. 위치 이동은 스냅샷 보간이 하므로 여기서는 전방/후방 대쉬
        /// 모션과 이펙트만 재생한다. 로컬(PlayerMoveController)과 같은 IsDash/IsBackDash 전이를 쓰고 길이도 같은 CombatTimings 값이다.
        /// </summary>
        public void PlayDash(bool backward)
        {
            // 사망 패킷 뒤에 늦게 도착한 알림이 Die 모션을 덮지 않게 한다.
            if (playerCharacterModel != null && playerCharacterModel.IsDead)
            {
                return;
            }

            isBackDash = backward;
            dashStartTime = Time.time + (float)(SnapshotInterpolationBuffer.InterpolationDelayMs / 1000.0);
            dashEndTime = dashStartTime + CombatTimings.DashDurationSeconds;
            dashEffectPlayed = false;
        }

        // 대쉬 구간 동안만 IsDash/IsBackDash를 켠다(둘이 동시에 켜지지 않게 한쪽만). 구간이 시작되는 프레임에 이펙트를 한 번 재생한다.
        private void UpdateDash()
        {
            bool dashing = Time.time >= dashStartTime && Time.time < dashEndTime;

            if (dashing && !dashEffectPlayed)
            {
                dashEffectPlayed = true;
                PlayDashEffect();
            }

            if (animator != null)
            {
                animator.SetBool(IsDashHash, dashing && !isBackDash);
                animator.SetBool(IsBackDashHash, dashing && isBackDash);
            }
        }

        private void PlayDashEffect()
        {
            if (ObjectPoolManager.Instance == null)
            {
                return;
            }

            Vector3 effectPosition = transform.TransformPoint(dashEffectPositionOffset);
            GameObject effectInstance = ObjectPoolManager.Instance.Get(AddressableAssetKey.Dash01.ToString(), effectPosition, transform.rotation);

            // 풀에서 돌려받은 인스턴스는 프리팹 원본 크기로 초기화되어 있으므로, dashEffectScale은 그 위에 곱해지는 배율이다.
            if (effectInstance != null && !Mathf.Approximately(dashEffectScale, 1f))
            {
                effectInstance.transform.localScale *= dashEffectScale;
            }
        }

        // 로컬(PlayerAttackController)과 같은 CombatTimings 표를 쓴다(등록되지 않은 WeaponType은 기본 길이).
        private static float GetStageDuration(WeaponType weaponType, int comboStage)
        {
            CombatTimings.TryGetAttackDuration((int)weaponType, comboStage, MaxComboStage, out float duration);
            return duration;
        }

        /// <summary>
        /// GameServerConnectManager.OnWorldSnapshot(Game_WorldSnapshot)으로 받은 위치를 서버 시각과 함께 보간 버퍼에 넣는다.
        /// </summary>
        public void AddSnapshot(double serverTimeMs, Vector3 position, float rotationY)
        {
            interpolation.Add(serverTimeMs, position, rotationY);
        }

        /// <summary>
        /// 스폰(Game_PlayerJoined)/부활처럼 보간 없이 즉시 해당 위치/회전으로 배치한다.
        /// </summary>
        public void Warp(Vector3 position, float rotationY)
        {
            interpolation.Reset(position, rotationY);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
        }

        public void SetPlayerId(long playerId)
        {
            PlayerId = playerId;
        }
    }
}
