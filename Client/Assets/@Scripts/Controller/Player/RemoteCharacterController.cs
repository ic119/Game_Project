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

        // PlayerAttackController.weaponAttackTimings와 같은 값이어야 한다(같은 Attack Layer BlendTree를 공유하므로
        // 무기 타입별 클립 길이가 동일하다). 로컬 콤보 진행을 직접 관리하는 PlayerAttackController와 달리 이쪽은
        // Game_AttackAnimationBroadcast로 받은 comboStage/weaponType 한 번만으로 재생 시간을 정해야 해서 별도로 둔다 -
        // 두 값이 갈라지면 원격 캐릭터의 모션 리셋 타이밍만 어긋나므로(치명적이지 않은 연출 문제) 공용 애셋으로
        // 묶지 않았다. weaponAttackTimings를 고치면 이 표도 같이 확인할 것.
        private static readonly (WeaponType WeaponType, float Attack1Duration, float Attack2Duration)[] WeaponAttackTimings =
        {
            (WeaponType.OneHanded, 16f / 30f, 16f / 30f),
            (WeaponType.TwoHanded, 18f / 30f, 18f / 30f),
            (WeaponType.Wand, 16f / 30f, 16f / 30f),
            (WeaponType.Spear, 16f / 30f, 20f / 30f),
        };

        // Attack Layer에 Attack1/Attack2 두 단계만 있다(PlayerAttackController.maxComboStage와 같은 값이어야 한다).
        private const int MaxComboStage = 2;
        private const string AttackLayerName = "Attack Layer";

        private readonly SnapshotInterpolationBuffer interpolation = new();
        private Animator animator;
        private float lastMovingTime = float.NegativeInfinity;
        private int attackLayerIndex = -1;
        private float attackAnimationEndTime = float.NegativeInfinity;

        /// <summary>
        /// 이 원격 캐릭터가 나타내는 서버측 플레이어 id. RemotePlayerManager가 스폰 직후 SetPlayerId로 채운다.
        /// 공격 대상 판정(PlayerAttackController)에서 콜라이더로부터 대상의 id를 즉시 얻는 데 쓴다.
        /// </summary>
        public long PlayerId { get; private set; }

        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");

        private void Awake()
        {
            interpolation.Reset(transform.position, transform.eulerAngles.y);
            animator = GetComponent<Animator>();
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

            // 공격 모션 재생 시간이 끝나면 Attack Layer 가중치를 다시 0으로 내린다(PlayerAttackController.ResetCombo와 동일한 목적).
            if (attackLayerIndex >= 0 && animator != null && animator.GetLayerWeight(attackLayerIndex) > 0f
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

            animator.SetFloat(WeaponIndexHash, (float)weaponType);
            animator.SetInteger(ComboIndexHash, comboStage);
            animator.SetLayerWeight(attackLayerIndex, 1f);
            attackAnimationEndTime = Time.time + GetStageDuration(weaponType, comboStage);
        }

        // PlayerAttackController.GetStageDuration과 같은 로직(등록되지 않은 WeaponType은 OneHanded 기준값으로 대체).
        private static float GetStageDuration(WeaponType weaponType, int comboStage)
        {
            foreach (var timing in WeaponAttackTimings)
            {
                if (timing.WeaponType == weaponType)
                {
                    return comboStage >= MaxComboStage ? timing.Attack2Duration : timing.Attack1Duration;
                }
            }

            return 16f / 30f;
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
