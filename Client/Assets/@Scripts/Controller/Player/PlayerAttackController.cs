using System;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 로컬 플레이어 전용. C 입력으로 2타 콤보 공격을 수행한다.
    /// 애니메이션은 BasicCharacterStance(Animator Controller)의 Attack Layer를 그대로 사용한다:
    /// AttackLayerIdle(ComboIndex=0) -[ComboIndex=1]-> Attack1 -[ComboIndex=2]-> Attack2 -[ComboIndex=0]-> AttackLayerIdle.
    /// Attack Layer는 기본 weight가 0이라(그래야 평소 Base Layer의 이동/대기 애니메이션을 가리지 않음),
    /// 콤보가 진행되는 동안만 1로 올렸다가 콤보가 끝나면 다시 0으로 내린다.
    /// 각 타수마다 전방 구체 판정(OverlapSphere)으로 가장 가까운 원격 플레이어를 찾아 공격 요청을 보낸다.
    /// 데미지 적용은 하지 않는다 - 서버가 중계한 Game_DamageBroadcast를 받는 쪽에서 처리한다.
    /// </summary>
    public class PlayerAttackController : MonoBehaviour
    {
        [Header("공격 판정")]
        [SerializeField] private KeyCode attackKey = KeyCode.C;
        [SerializeField, Min(0f)] private float attackRange = 1.5f;
        [SerializeField, Min(0f)] private float attackRadius = 1.0f;

        [Header("이펙트")]
        [Tooltip("스윙/임팩트 이펙트를 스폰할 때 기준 위치(캐릭터 발밑 기준 transform.position)에 더할 높이(초). " +
            "캐릭터 원점이 발밑이라 0이면 이펙트가 바닥에 붙어 보이므로, 무기/상체 높이에 맞춰 올려준다.")]
        [SerializeField, Min(0f)] private float effectHeight = 1f;
        [Tooltip("이펙트 프리팹에 이미 적용된 크기(예: 0.7) 위에 추가로 곱해지는 배율. 1이면 프리팹 크기 그대로.")]
        [SerializeField, Min(0.01f)] private float effectScale = 0.65f;

        [Header("콤보 (BasicCharacterStance/Attack Layer 참고)")]
        [Tooltip("콤보 최대 타수. Attack Layer에 Attack1/Attack2 두 단계만 있어 2로 둔다.")]
        [SerializeField, Min(1)] private int maxComboStage = 2;
        [Tooltip("무기 타입별 Attack1/Attack2 애니메이션 길이(초). Attack Layer의 Attack1/Attack2 상태는 " +
            "WeaponIndex 파라미터를 기준으로 한 BlendTree라 무기마다 실제 재생되는 클립이 다르고 길이도 다르므로" +
            "(예: Spear는 Attack01 16프레임/Attack02 20프레임으로 서로 다름), 무기 타입마다 따로 값을 맞춘다. " +
            "목록에 없는 WeaponType은 기본값(16프레임/30fps)으로 처리한다.")]
        [SerializeField] private WeaponAttackTiming[] weaponAttackTimings =
        {
            new WeaponAttackTiming { weaponType = WeaponType.OneHanded, attack1Duration = 16f / 30f, attack2Duration = 16f / 30f },
            new WeaponAttackTiming { weaponType = WeaponType.TwoHanded, attack1Duration = 18f / 30f, attack2Duration = 18f / 30f },
            new WeaponAttackTiming { weaponType = WeaponType.Shield, attack1Duration = 16f / 30f, attack2Duration = 16f / 30f },
            new WeaponAttackTiming { weaponType = WeaponType.Wand, attack1Duration = 16f / 30f, attack2Duration = 16f / 30f },
            new WeaponAttackTiming { weaponType = WeaponType.Spear, attack1Duration = 16f / 30f, attack2Duration = 20f / 30f },
        };
        [Tooltip("각 단계 시작 후 이 시간이 지나야 다음 입력을 콤보 연계로 인정한다(스윙 시작 직후 캔슬 방지).")]
        [SerializeField, Min(0f)] private float comboInputGuard = 0.15f;
        [Tooltip("마지막 타수(2콤보) 공격이 끝난 뒤 다음 공격을 다시 받아들이기까지의 딜레이(초). 애니메이션은 이 딜레이와 무관하게 공격 종료 즉시 Idle로 돌아가고, 이 값은 공격 판정(RequestAttack)이 곧바로 겹치지 않도록 입력만 잠근다.")]
        [SerializeField, Min(0f)] private float comboFinishDelay = 0.25f;

        [Serializable]
        private struct WeaponAttackTiming
        {
            public WeaponType weaponType;
            [Min(0.05f)] public float attack1Duration;
            [Min(0.05f)] public float attack2Duration;
        }

        private const string AttackLayerName = "Attack Layer";
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");
        private static readonly Collider[] overlapBuffer = new Collider[16];

        private Animator animator;
        private int attackLayerIndex = -1;
        private PlayerCharacterModel playerCharacterModel;

        private int comboStage;
        private float stageStartTime;
        private float currentStageDuration;
        private float nextAttackReadyTime;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator != null)
            {
                attackLayerIndex = animator.GetLayerIndex(AttackLayerName);
            }

            playerCharacterModel = GetComponent<PlayerCharacterModel>();
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnDamageReceived += HandleDamageReceived;
            }
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnDamageReceived -= HandleDamageReceived;
            }
        }

        private void Update()
        {
            // 애니메이션 클립 길이(currentStageDuration, 무기 타입별로 다름)가 끝나는 즉시 Idle로 되돌린다.
            // 클립이 끝난 뒤에도 자세를 유지하면 캐릭터가 공격 자세로 멈춰있는 것처럼 보이므로 유예 없이 바로 리셋한다.
            if (comboStage > 0 && Time.time >= stageStartTime + currentStageDuration)
            {
                ResetCombo();
            }

            if (!Input.GetKeyDown(attackKey))
            {
                return;
            }

            if (comboStage == 0)
            {
                // 마지막 타수(콤보 완료) 직후에는 comboFinishDelay가 지나기 전까지 새 공격을 받지 않는다.
                if (Time.time < nextAttackReadyTime)
                {
                    return;
                }

                StartCombo();
            }
            else if (comboStage < maxComboStage && Time.time >= stageStartTime + comboInputGuard)
            {
                AdvanceCombo();
            }
            // 가드 시간 이전(너무 이른 연타)이거나 이미 마지막 타수인 입력은 무시한다.
        }

        private void StartCombo()
        {
            comboStage = 1;
            stageStartTime = Time.time;
            currentStageDuration = GetStageDuration(comboStage);
            UpdateNextAttackReadyTime();

            if (animator != null && attackLayerIndex >= 0)
            {
                SyncWeaponIndex();
                animator.SetLayerWeight(attackLayerIndex, 1f);
                animator.SetInteger(ComboIndexHash, comboStage);
            }

            PlaySwingEffect();
            RequestAttack();
        }

        private void AdvanceCombo()
        {
            comboStage++;
            stageStartTime = Time.time;
            currentStageDuration = GetStageDuration(comboStage);
            UpdateNextAttackReadyTime();

            if (animator != null && attackLayerIndex >= 0)
            {
                SyncWeaponIndex();
                animator.SetInteger(ComboIndexHash, comboStage);
            }

            PlaySwingEffect();
            RequestAttack();
        }

        /// <summary>
        /// Attack Layer의 Attack1/Attack2 상태는 무기 타입별 클립(예: SingleSword/THS/SwordAndShield/MagicWand/Spear의
        /// Attack01·Attack02)을 담은 BlendTree이고, WeaponIndex 파라미터(WeaponType의 enum 값)로 그중 하나를 정확히
        /// 골라 재생한다(임계값이 정수로 정확히 일치해 블렌딩 없이 하나만 재생됨). 매 콤보 단계 시작마다 현재 장착 무기로
        /// 동기화해, 공격 도중 장비를 바꾸는 경우에도 다음 콤보 단계부터는 항상 최신 무기 애니메이션이 재생되게 한다.
        /// </summary>
        private void SyncWeaponIndex()
        {
            if (playerCharacterModel == null)
            {
                return;
            }

            animator.SetFloat(WeaponIndexHash, (float)playerCharacterModel.CurrentWeaponType);
        }

        /// <summary>
        /// 현재 장착 무기(playerCharacterModel.CurrentWeaponType)와 콤보 단계에 맞는 애니메이션 길이를 반환한다.
        /// weaponAttackTimings에 등록되지 않은 WeaponType이면 SingleSword 기준값(16프레임/30fps)으로 대체한다.
        /// </summary>
        private float GetStageDuration(int stage)
        {
            WeaponType weaponType = playerCharacterModel != null ? playerCharacterModel.CurrentWeaponType : WeaponType.None;

            foreach (WeaponAttackTiming timing in weaponAttackTimings)
            {
                if (timing.weaponType == weaponType)
                {
                    return stage >= maxComboStage ? timing.attack2Duration : timing.attack1Duration;
                }
            }

            DebugLogManager.GenerateErrorMessage<PlayerAttackController>($"WeaponType '{weaponType}'에 대응하는 공격 타이밍 설정이 weaponAttackTimings에 없습니다. 기본값을 사용합니다.");
            return 16f / 30f;
        }

        private void UpdateNextAttackReadyTime()
        {
            if (comboStage >= maxComboStage)
            {
                nextAttackReadyTime = stageStartTime + currentStageDuration + comboFinishDelay;
            }
        }

        private void ResetCombo()
        {
            comboStage = 0;

            if (animator != null && attackLayerIndex >= 0)
            {
                animator.SetInteger(ComboIndexHash, 0);
                animator.SetLayerWeight(attackLayerIndex, 0f);
            }
        }

        private void RequestAttack()
        {
            Vector3 origin = GetAttackOrigin();
            int hitCount = Physics.OverlapSphereNonAlloc(origin, attackRadius, overlapBuffer);

            long? targetId = FindNearestTargetId(hitCount);
            if (!targetId.HasValue)
            {
                return;
            }

            GameServerConnectManager.Instance?.SendAttack(targetId.Value);
        }

        private Vector3 GetAttackOrigin()
        {
            return transform.position + transform.forward * attackRange;
        }

        // 이펙트 전용 높이 보정. 히트 판정(GetAttackOrigin)에는 영향을 주지 않도록 별도 헬퍼로 분리한다.
        private Vector3 ApplyEffectHeight(Vector3 position)
        {
            return position + Vector3.up * effectHeight;
        }

        /// <summary>
        /// 휘두르는 순간(명중 여부와 무관) 재생하는 이펙트. RequestAttack과 달리 대상을 못 찾아도(허공에 휘둘러도)
        /// 항상 재생해야 하므로, 콤보 타수마다(StartCombo/AdvanceCombo) 독립적으로 호출한다.
        /// </summary>
        private void PlaySwingEffect()
        {
            if (playerCharacterModel == null || WeaponVfxManager.Instance == null)
            {
                return;
            }

            WeaponVfxManager.Instance.PlaySwingEffect(playerCharacterModel.CurrentWeaponType, ApplyEffectHeight(GetAttackOrigin()), transform.rotation, effectScale);
        }

        /// <summary>
        /// Game_DamageBroadcast는 전원에게 오지만, 여기서는 "내가 명중시킨" 경우만 처리해 대상 위치에
        /// 임팩트 이펙트를 재생한다. 다른 플레이어의 무기 타입은 서버가 아직 전달해주지 않아(GamePlayerInfo에
        /// WeaponType이 없음) 내가 맞은 경우/남이 남을 때린 경우는 여기서 재생할 수 없다 - 알려진 한계.
        /// </summary>
        private void HandleDamageReceived(GameDamageBroadcastPacket packet)
        {
            if (playerCharacterModel == null || SaveDataManager.Instance == null)
            {
                return;
            }

            if (packet.AttackerId != SaveDataManager.Instance.SelectedCharacterId)
            {
                return;
            }

            if (RemotePlayerManager.Instance == null || !RemotePlayerManager.Instance.TryGetRemotePlayer(packet.TargetId, out RemoteCharacterController target))
            {
                return;
            }

            WeaponVfxManager.Instance?.PlayImpactEffect(playerCharacterModel.CurrentWeaponType, ApplyEffectHeight(target.transform.position), target.transform.rotation, effectScale);
        }

        private long? FindNearestTargetId(int hitCount)
        {
            long? bestId = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                RemoteCharacterController remote = hit.GetComponentInParent<RemoteCharacterController>();
                if (remote == null)
                {
                    continue;
                }

                float distanceSqr = (remote.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestId = remote.PlayerId;
                }
            }

            return bestId;
        }
    }
}
