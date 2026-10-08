using System;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 로컬 플레이어 전용. 숫자 키 1~4로 액티브 스킬을 쓴다(해금 레벨 3/6/9/12, 무기 종류별 스킬은 SkillTable).
    /// 판정(해금/마나/쿨다운/범위/피해)은 모두 서버가 하고, 여기서는 입력 -> 요청 전송, 시전 모션/이동·공격 잠금, 쿨다운 표시용 상태만 맡는다.
    /// 응답을 기다리면 모션이 늦으므로 로컬 사전 검사(무기/레벨/마나/쿨다운)를 통과하면 곧바로 시전을 시작하고(낙관적 시작),
    /// 서버가 거부(Game_SkillResult)하면 시전을 취소하고 쿨다운을 서버 값으로 맞춘다.
    /// 시전 중에는 이동/기본 공격/다른 스킬이 잠기고, 대쉬(Space)로만 끊을 수 있다(서버도 대쉬 성공 시 남은 타격을 취소한다).
    /// 애니메이션은 BasicCharacterStance의 Attack Layer를 쓴다: SkillIndex 파라미터가 스킬 모션 상태로 전환시키고, 끝나면 0으로 되돌린다.
    /// </summary>
    public class PlayerSkillController : MonoBehaviour
    {
        [Header("입력")]
        [Tooltip("슬롯 1~4에 대응하는 키. 순서대로 슬롯 1, 2, 3, 4다.")]
        [SerializeField] private KeyCode[] skillKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

        private const string AttackLayerName = "Attack Layer";
        private static readonly int SkillIndexHash = Animator.StringToHash("SkillIndex");
        private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");

        private Animator animator;
        private int attackLayerIndex = -1;
        private PlayerCharacterModel playerCharacterModel;
        private PlayerMoveController moveController;
        private PlayerAttackController attackController;
        private ManaComponent manaComponent;

        // 슬롯별 쿨다운이 끝나는 시각(Time.time)과 마지막으로 시작한 쿨다운 전체 길이. 인덱스 0은 쓰지 않는다(슬롯은 1부터).
        private readonly float[] cooldownEndTime = new float[SkillTable.SlotCount + 1];
        private readonly float[] cooldownDuration = new float[SkillTable.SlotCount + 1];

        private int castingSlot;
        private float castEndTime;

        /// <summary>시전이 시작될 때(슬롯, 스킬 정보). 스킬 이펙트/UI가 구독한다.</summary>
        public event Action<int, SkillTable.Entry> SkillCast;

        /// <summary>서버가 시전을 거부했을 때(슬롯, 사유). 쿨다운/시전 취소 처리를 마친 뒤 발생하며, UI가 마나 부족 같은 안내를 띄우는 데 쓴다.</summary>
        public event Action<int, GameSkillCastStatus> SkillRejected;

        /// <summary>시전 모션 중인지.</summary>
        public bool IsCasting => castingSlot != 0;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator != null)
            {
                attackLayerIndex = animator.GetLayerIndex(AttackLayerName);
            }

            playerCharacterModel = GetComponent<PlayerCharacterModel>();
            moveController = GetComponent<PlayerMoveController>();
            attackController = GetComponent<PlayerAttackController>();
            manaComponent = GetComponent<ManaComponent>();
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnSkillResultReceived += HandleSkillResult;
            }

            if (moveController != null)
            {
                moveController.Dashed += HandleDashed;
            }
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnSkillResultReceived -= HandleSkillResult;
            }

            if (moveController != null)
            {
                moveController.Dashed -= HandleDashed;
            }

            if (IsCasting)
            {
                EndCast();
            }
        }

        private void Update()
        {
            if (IsCasting)
            {
                // 모션이 끝났거나, 사망/피격 모션이 시전 모션을 덮었으면 끝낸다(피격 모션이 끝난 뒤 SkillIndex가 남아 스킬이 다시 재생되면 안 된다).
                bool interrupted = playerCharacterModel != null && (playerCharacterModel.IsDead || playerCharacterModel.IsHitReactionPlaying);
                if (interrupted || Time.time >= castEndTime)
                {
                    EndCast();
                }

                return;
            }

            if (InputBlocker.IsBlocked || playerCharacterModel == null || playerCharacterModel.IsDead)
            {
                return;
            }

            for (int i = 0; i < skillKeys.Length && i < SkillTable.SlotCount; i++)
            {
                if (Input.GetKeyDown(skillKeys[i]))
                {
                    TryCast(i + 1);
                    return;
                }
            }
        }

        #region Method
        /// <summary>슬롯의 남은 쿨다운(초). 쿨다운 중이 아니면 0.</summary>
        public float GetCooldownRemaining(int slot)
        {
            return slot is >= 1 and <= SkillTable.SlotCount ? Mathf.Max(0f, cooldownEndTime[slot] - Time.time) : 0f;
        }

        /// <summary>슬롯의 마지막 쿨다운 전체 길이(초). 쿨다운 표시(채워지는 비율)의 분모로 쓴다.</summary>
        public float GetCooldownDuration(int slot)
        {
            return slot is >= 1 and <= SkillTable.SlotCount ? cooldownDuration[slot] : 0f;
        }

        // 로컬에서 쓸 수 있어 보이면 시전을 시작한다. 서버가 같은 조건을 다시 검사해 거부할 수 있다(그러면 HandleSkillResult가 되돌린다).
        private void TryCast(int slot)
        {
            WeaponType weapon = playerCharacterModel.CurrentWeaponType;
            if (!SkillTable.TryGet((int)weapon, slot, out SkillTable.Entry skill)
                || playerCharacterModel.Level < skill.UnlockLevel
                || GetCooldownRemaining(slot) > 0f
                || (manaComponent != null && manaComponent.CurrentMp < skill.ManaCost)
                || (moveController != null && moveController.IsDashing)
                || playerCharacterModel.IsHitReactionPlaying)
            {
                return;
            }

            // 가까운 몬스터가 있으면 그 쪽으로 몸을 돌린다(기본 공격의 방향 보조와 같다). 방향을 정한 뒤 그 방향을 서버에 알린다.
            attackController?.InterruptForSkill();
            attackController?.FaceNearestMonster(skill.CastLockSeconds);
            float rotationY = transform.eulerAngles.y;

            if (GameServerConnectManager.Instance == null || !GameServerConnectManager.Instance.SendSkill(slot, rotationY))
            {
                return;
            }

            StartCast(slot, skill);
        }

        private void StartCast(int slot, SkillTable.Entry skill)
        {
            castingSlot = slot;
            castEndTime = Time.time + skill.CastLockSeconds;
            cooldownEndTime[slot] = Time.time + skill.CooldownSeconds;
            cooldownDuration[slot] = skill.CooldownSeconds;

            moveController?.LockMovement(skill.CastLockSeconds);
            if (attackController != null)
            {
                attackController.SkillCasting = true;

                // 이펙트가 시전 모션보다 오래 이어지는 스킬은 시전이 끝난 뒤에도 일정 시간 기본 공격을 막는다(SkillVfxEntry.attackLockSeconds).
                float attackLockSeconds = SkillVfxManager.Instance != null ? SkillVfxManager.Instance.GetAttackLockSeconds(skill) : skill.CastLockSeconds;
                attackController.BlockAttackUntil(Time.time + attackLockSeconds);
            }

            playerCharacterModel.CancelHitReaction();

            if (animator != null && attackLayerIndex >= 0)
            {
                animator.SetFloat(WeaponIndexHash, (float)playerCharacterModel.CurrentWeaponType);
                animator.SetLayerWeight(attackLayerIndex, 1f);
                animator.SetInteger(SkillIndexHash, skill.AnimatorSkillIndex);
            }

            // 이펙트는 서버 응답을 기다리지 않고 모션과 함께 바로 재생한다. 서버가 거부하면 모션과 함께 끊기지만, 이미 터진 이펙트는 짧게 보이고 끝난다.
            SkillVfxManager.Instance?.PlaySkill(skill, transform.position, transform.rotation, showTelegraph: true);

            SkillCast?.Invoke(slot, skill);
        }

        // 시전 모션을 끝낸다: 잠금을 풀고 SkillIndex를 0으로, Attack Layer 가중치를 0으로 되돌린다.
        private void EndCast()
        {
            castingSlot = 0;
            castEndTime = 0f;

            moveController?.UnlockMovement();
            if (attackController != null)
            {
                attackController.SkillCasting = false;
            }

            if (animator != null && attackLayerIndex >= 0)
            {
                animator.SetInteger(SkillIndexHash, 0);

                // 피격 모션이 Attack Layer를 쓰는 중이면(PlayerCharacterModel이 가중치를 직접 내린다) 건드리지 않는다.
                if (playerCharacterModel == null || !playerCharacterModel.IsHitReactionPlaying)
                {
                    animator.SetLayerWeight(attackLayerIndex, 0f);
                }
            }
        }

        // 대쉬는 진행 중인 시전을 끊는다. 서버도 대쉬가 승인되면 남은 타격을 취소한다(GameRoom.CancelSkillCast).
        private void HandleDashed()
        {
            if (IsCasting)
            {
                EndCast();
            }

            // 대쉬로 시전을 끊으면 이펙트가 남아 있어도 기본 공격을 다시 할 수 있고, 서버도 남은 타격을 취소하므로 바닥 범위 표시도 거둔다.
            attackController?.ClearSkillAttackBlock();
            SkillVfxManager.Instance?.CancelTelegraph();
            SkillVfxManager.Instance?.CancelSkill(SkillVfxManager.LocalCasterId);
        }

        // 서버의 시전 결과. 승인이면 쿨다운을 서버 값으로 맞추고, 거부면 시전을 취소하고 사유에 맞게 쿨다운을 되돌린다.
        private void HandleSkillResult(GameSkillResultPacket packet)
        {
            if (packet.Slot is < 1 or > SkillTable.SlotCount)
            {
                return;
            }

            switch (packet.Status)
            {
                case GameSkillCastStatus.Accepted:
                    cooldownEndTime[packet.Slot] = Time.time + packet.CooldownSeconds;
                    cooldownDuration[packet.Slot] = packet.CooldownSeconds;
                    return;

                case GameSkillCastStatus.OnCooldown:
                    // 서버가 아는 남은 쿨다운으로 맞춘다(로컬이 짧게 알고 있었던 경우).
                    cooldownEndTime[packet.Slot] = Time.time + packet.CooldownSeconds;
                    break;

                case GameSkillCastStatus.Casting:
                    // 시전 잠금에 걸린 요청은 쿨다운을 시작하지 않았다 - 로컬이 낙관적으로 시작한 쿨다운을 되돌린다.
                    cooldownEndTime[packet.Slot] = 0f;
                    break;

                default:
                    // 마나 부족/해금 전/스킬 없음/사망/잘못된 요청: 서버는 쿨다운을 시작하지 않았다.
                    cooldownEndTime[packet.Slot] = 0f;
                    break;
            }

            if (IsCasting && castingSlot == packet.Slot)
            {
                EndCast();

                // 거부된 시전이 걸어 둔 기본 공격 막힘도 푼다(다른 슬롯의 거부가 진행 중인 시전의 막힘을 풀면 안 되므로 같은 슬롯일 때만).
                attackController?.ClearSkillAttackBlock();
                SkillVfxManager.Instance?.CancelTelegraph();
                SkillVfxManager.Instance?.CancelSkill(SkillVfxManager.LocalCasterId);
            }

            SkillRejected?.Invoke(packet.Slot, packet.Status);
        }
        #endregion
    }
}
