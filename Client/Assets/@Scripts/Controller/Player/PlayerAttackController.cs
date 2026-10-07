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

        [Header("공격 방향 보조")]
        [Tooltip("켜면 공격을 시작할 때 주변에 몬스터가 있으면 그 쪽으로 몸을 자동으로 돌린다(이동 방향과 달라도). 콤보 도중에는 같은 대상을 유지한다.")]
        [SerializeField] private bool aimAssist = true;
        [Tooltip("이 반경(m) 안의 가장 가까운 몬스터를 보조 대상으로 삼는다. 공격 판정(attackRange+attackRadius)에 닿는 거리보다 크면 돌기만 하고 못 맞힌다.")]
        [SerializeField, Min(0f)] private float aimAssistRange = 2.8f;

        [Header("타격감")]
        [Tooltip("내 공격이 명중했을 때 공격 애니메이션을 이 시간(초)만큼 멈춰 타격감을 준다. 0이면 끈다.")]
        [SerializeField, Range(0f, 0.15f)] private float hitStopSeconds = 0.05f;

        [Header("이펙트")]
        [Tooltip("스윙/임팩트 이펙트를 스폰할 때 기준 위치(캐릭터 발밑 기준 transform.position)에 더할 높이(초). " +
            "캐릭터 원점이 발밑이라 0이면 이펙트가 바닥에 붙어 보이므로, 무기/상체 높이에 맞춰 올려준다.")]
        [SerializeField, Min(0f)] private float effectHeight = 1f;
        [Tooltip("이펙트 프리팹에 이미 적용된 크기(예: 0.7) 위에 추가로 곱해지는 배율. 1이면 프리팹 크기 그대로.")]
        [SerializeField, Min(0.01f)] private float effectScale = 0.65f;

        [Header("콤보 (BasicCharacterStance/Attack Layer 참고)")]
        [Tooltip("콤보 최대 타수. Attack Layer에 Attack1/Attack2 두 단계만 있어 2로 둔다.")]
        [SerializeField, Min(1)] private int maxComboStage = 2;
        [Tooltip("각 단계 시작 후 이 시간이 지나야 다음 입력을 콤보 연계로 인정한다(스윙 시작 직후 캔슬 방지).")]
        [SerializeField, Min(0f)] private float comboInputGuard = CombatTimings.ComboInputGuardSeconds;
        [Tooltip("마지막 타수(2콤보) 공격이 끝난 뒤 다음 공격을 다시 받아들이기까지의 딜레이(초). 애니메이션은 이 딜레이와 무관하게 공격 종료 즉시 Idle로 돌아가고, 이 값은 공격 판정(RequestAttack)이 곧바로 겹치지 않도록 입력만 잠근다.")]
        [SerializeField, Min(0f)] private float comboFinishDelay = 0.25f;

        [Header("완드 (원거리)")]
        [Tooltip("완드 공격의 사거리(m). 정면 허용 각도 안의 가장 가까운 대상에게 투사체를 날린다. 서버 최대 공격 사거리(CombatTuning.MaxAttackRange 5m)보다 짧아야 한다.")]
        [SerializeField, Min(0.5f)] private float wandRange = 4f;
        [Tooltip("완드가 대상으로 삼는 정면 허용 각도(좌우 각각, 도). 공격 방향 보조가 가까운 몬스터 쪽으로 먼저 몸을 돌리므로 보통 그 대상이 이 안에 들어온다.")]
        [SerializeField, Range(10f, 180f)] private float wandAimHalfAngle = 70f;
        [Tooltip("완드 1타 후 다음 입력(2타)을 받기까지의 후딜(초, 1타 시작 기준). 이 시간 전에 누른 입력은 버퍼에 담았다가 후딜이 끝나는 순간 2타로 나간다. 1타 동작 길이보다 짧아야 2타를 칠 수 있다.")]
        [SerializeField, Min(0f)] private float wandFirstHitRecovery = 0.45f;
        [Tooltip("투사체와 시전 이펙트가 나가는 지팡이 끝 위치(캐릭터 기준 로컬 오프셋: x 오른쪽, y 위, z 앞). 다른 플레이어 화면에서도 같은 값을 쓴다(ProjectileVfxManager).")]
        [SerializeField] private Vector3 wandMuzzleOffset = new Vector3(0.5f, 0.5f, 1.0f);

        private const string AttackLayerName = "Attack Layer";
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");
        private static readonly Collider[] overlapBuffer = new Collider[16];
        // 공격 방향 보조 전용 버퍼. 바로 뒤에 이어지는 RequestAttack이 overlapBuffer를 쓰므로 따로 둔다.
        private static readonly Collider[] aimAssistBuffer = new Collider[16];

        private Animator animator;
        private int attackLayerIndex = -1;
        private PlayerCharacterModel playerCharacterModel;
        private PlayerMoveController moveController;
        private RemoteMonsterController aimAssistTarget;
        private Coroutine hitStopRoutine;

        /// <summary>
        /// 공격 판정(RequestAttack)이 몬스터를 대상으로 찾을 때마다 발생한다. UI_GameSceneView가
        /// 몬스터 이름/등급/체력바를 갱신하는 데 사용한다(GameSceneManager가 중계).
        /// </summary>
        public event Action<RemoteMonsterController> MonsterTargeted;

        private int comboStage;
        private float stageStartTime;
        private float currentStageDuration;
        private float nextAttackReadyTime;

        // 완드 1타 후딜 중에 눌린 입력(후딜이 끝나면 2타로 나간다). 콤보가 끝나거나 새로 시작하면 비운다.
        private bool wandAttackBuffered;

        private bool IsWand => playerCharacterModel != null && playerCharacterModel.CurrentWeaponType == WeaponType.Wand;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator != null)
            {
                attackLayerIndex = animator.GetLayerIndex(AttackLayerName);
            }

            playerCharacterModel = GetComponent<PlayerCharacterModel>();
            moveController = GetComponent<PlayerMoveController>();
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnDamageReceived += HandleDamageReceived;
                GameServerConnectManager.Instance.OnMonsterDamaged += HandleMonsterDamaged;
                GameServerConnectManager.Instance.OnAttackAnimationReceived += HandleAttackAnimationReceived;
            }
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnDamageReceived -= HandleDamageReceived;
                GameServerConnectManager.Instance.OnMonsterDamaged -= HandleMonsterDamaged;
                GameServerConnectManager.Instance.OnAttackAnimationReceived -= HandleAttackAnimationReceived;
            }

            // 히트스톱 도중에 비활성화돼도 애니메이션이 멈춘 채 남지 않게 되돌린다.
            EndHitStop();
        }

        private void Update()
        {
            // 애니메이션 클립 길이(currentStageDuration, 무기 타입별로 다름)가 끝나는 즉시 Idle로 되돌린다.
            // 클립이 끝난 뒤에도 자세를 유지하면 캐릭터가 공격 자세로 멈춰있는 것처럼 보이므로 유예 없이 바로 리셋한다.
            if (comboStage > 0 && Time.time >= stageStartTime + currentStageDuration)
            {
                ResetCombo();
            }

            // 사망 중에는 공격 모션/판정/요청을 시작하지 않는다(컨트롤러가 꺼지기 전 같은 프레임에 들어온 입력까지 막는다).
            bool isDead = playerCharacterModel != null && playerCharacterModel.IsDead;

            // 완드: 1타 후딜 중에 눌러 둔 입력이 후딜이 끝나는 순간 2타로 나간다(1타 동작이 끝나기 전일 때만 - 끝나면 콤보가 리셋되며 버퍼도 비워진다).
            if (wandAttackBuffered && comboStage == 1 && !isDead && IsWand && Time.time >= stageStartTime + GetWandRecoverySeconds())
            {
                wandAttackBuffered = false;
                AdvanceCombo();
                return;
            }

            // 채팅 입력 중처럼 게임플레이 입력이 막혀 있으면(InputBlocker) 공격 키를 읽지 않는다. 위의 콤보 종료 처리는 계속 돈다.
            if (InputBlocker.IsBlocked || !Input.GetKeyDown(attackKey))
            {
                return;
            }

            if (isDead)
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
            else if (comboStage < maxComboStage)
            {
                // 완드의 1타는 후딜(wandFirstHitRecovery)이 지나야 다음 입력을 받는다. 그 전에 누른 입력은 버리지 않고 버퍼에 담는다.
                bool isWandFirstHit = IsWand && comboStage == 1;
                float guard = isWandFirstHit ? GetWandRecoverySeconds() : comboInputGuard;

                if (Time.time >= stageStartTime + guard)
                {
                    AdvanceCombo();
                }
                else if (isWandFirstHit)
                {
                    wandAttackBuffered = true;
                }
                // 그 밖에 가드 시간 이전(너무 이른 연타)인 입력은 무시한다.
            }
            // 이미 마지막 타수인 입력은 무시한다.
        }

        // 완드 1타 후딜. 1타 동작 길이보다 길게 설정돼 2타가 영영 불가능해지지 않도록 동작이 끝나기 조금 전으로 제한한다.
        private float GetWandRecoverySeconds()
        {
            return Mathf.Min(wandFirstHitRecovery, Mathf.Max(comboInputGuard, currentStageDuration - 0.05f));
        }

        private void StartCombo()
        {
            // 피격 모션을 재생 중이면 끝내고 공격으로 넘어간다 - 피격 모션이 끝나기를 기다리느라 공격이 늦어지면 조작감이 나빠진다.
            // ComboIndex를 올리기 전에 불러야 한다(CancelHitReaction은 ComboIndex가 0일 때만 Attack Layer 가중치를 내린다).
            if (playerCharacterModel != null)
            {
                playerCharacterModel.CancelHitReaction();
            }

            wandAttackBuffered = false;
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

            // 스윙 이펙트와 판정은 캐릭터가 바라보는 방향을 쓰므로, 그 전에 방향을 맞춘다.
            AssistAim();
            ExecuteAttack();
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

            AssistAim();
            ExecuteAttack();
        }

        /// <summary>
        /// 이번 타수의 연출과 공격 요청을 한 번에 수행한다. 근접 무기는 스윙 이펙트 -> 공격 모션 중계 -> 전방 구체 판정 요청 순서이고,
        /// 완드(원거리)는 대상을 먼저 찾아 시전/투사체 연출, 공격 모션 중계(대상 포함), 공격 요청을 보낸다(PerformWandAttack).
        /// </summary>
        private void ExecuteAttack()
        {
            if (IsWand)
            {
                PerformWandAttack();
                return;
            }

            PlaySwingEffect();
            BroadcastAttackAnimation();
            RequestAttack();
        }

        /// <summary>
        /// 완드 공격 한 번: 정면 허용 각도 안의 가장 가까운 대상(wandRange 이내)을 찾아 지팡이 끝에서 투사체를 날리고, 서버에 공격 모션(대상
        /// 포함)과 공격 요청을 보낸다. 피해 판정은 서버가 요청을 받는 즉시 하므로 투사체는 순수 연출이다. 대상이 없으면 허공으로 날린다.
        /// </summary>
        private void PerformWandAttack()
        {
            FindWandTarget(out AttackTargetKind kind, out long targetId, out Transform targetTransform, out RemoteMonsterController targetMonster);

            Vector3 muzzle = transform.TransformPoint(wandMuzzleOffset);
            ProjectileVfxManager.Instance?.FireRanged(WeaponType.Wand, muzzle, transform.forward, targetTransform);

            GameServerConnectManager.Instance?.SendAttackAnimation(comboStage, WeaponType.Wand, kind, targetId);

            switch (kind)
            {
                case AttackTargetKind.Monster:
                    GameServerConnectManager.Instance?.SendMonsterAttack(targetId);
                    if (targetMonster != null)
                    {
                        MonsterTargeted?.Invoke(targetMonster);
                    }
                    break;

                case AttackTargetKind.Player:
                    GameServerConnectManager.Instance?.SendAttack(targetId);
                    break;
            }
        }

        /// <summary>
        /// 완드가 노릴 대상을 찾는다: wandRange 안의 살아 있는 몬스터/원격 플레이어 중, 정면에서 wandAimHalfAngle 이내인 가장 가까운 하나.
        /// 없으면 kind = None.
        /// </summary>
        private void FindWandTarget(out AttackTargetKind kind, out long targetId, out Transform targetTransform, out RemoteMonsterController targetMonster)
        {
            kind = AttackTargetKind.None;
            targetId = 0;
            targetTransform = null;
            targetMonster = null;

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, wandRange, overlapBuffer);
            Vector3 forward = transform.forward;
            forward.y = 0f;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                AttackTargetKind candidateKind;
                long candidateId;
                Transform candidateTransform;
                RemoteMonsterController candidateMonster = null;

                RemoteCharacterController remotePlayer = hit.GetComponentInParent<RemoteCharacterController>();
                if (remotePlayer != null)
                {
                    candidateKind = AttackTargetKind.Player;
                    candidateId = remotePlayer.PlayerId;
                    candidateTransform = remotePlayer.transform;
                }
                else
                {
                    // 사망 연출 중인 몬스터는 대상에서 뺀다(TryFindNearestTarget과 같은 이유).
                    RemoteMonsterController monster = hit.GetComponentInParent<RemoteMonsterController>();
                    if (monster == null || monster.IsDead)
                    {
                        continue;
                    }

                    candidateKind = AttackTargetKind.Monster;
                    candidateId = monster.MonsterId;
                    candidateTransform = monster.transform;
                    candidateMonster = monster;
                }

                Vector3 toTarget = candidateTransform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toTarget) > wandAimHalfAngle)
                {
                    continue;
                }

                float distanceSqr = toTarget.sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    kind = candidateKind;
                    targetId = candidateId;
                    targetTransform = candidateTransform;
                    targetMonster = candidateMonster;
                }
            }
        }

        /// <summary>
        /// 공격을 시작하는 순간 가까운 몬스터가 있으면 그 쪽으로 몸을 돌린다. 공격 판정은 캐릭터 앞쪽 구체라서, 이동 방향과 적이 있는
        /// 방향이 달라도 헛스윙하지 않게 해 준다. 몬스터만 대상으로 한다 - 다른 플레이어 쪽으로 의도치 않게 돌지 않게 하기 위함이다.
        /// 콤보 2타는 1타에서 정한 대상을 (아직 가깝고 살아 있으면) 그대로 유지한다.
        /// </summary>
        private void AssistAim()
        {
            if (!aimAssist || moveController == null)
            {
                return;
            }

            RemoteMonsterController target = ResolveAimAssistTarget();
            if (target == null)
            {
                return;
            }

            // 방향 잠금은 이번 공격 동작이 끝날 때까지 - 그동안 이동 입력이 몸을 다시 돌려놓지 않는다.
            moveController.FaceDirection(target.transform.position - transform.position, currentStageDuration);
        }

        private RemoteMonsterController ResolveAimAssistTarget()
        {
            // 완드는 사거리(wandRange)까지 대상으로 삼는다 - 근접 무기보다 멀리 있는 몬스터 쪽으로도 몸을 돌려야 쏠 수 있다.
            float range = IsWand ? wandRange : aimAssistRange;

            // 이어지는 타수는 직전 대상을 유지한다(두 타 사이에 대상이 흔들리지 않게). 조금 멀어져도 한동안은 놓지 않는다.
            if (aimAssistTarget != null && !aimAssistTarget.IsDead
                && (aimAssistTarget.transform.position - transform.position).sqrMagnitude <= range * range * 1.5625f)
            {
                return aimAssistTarget;
            }

            aimAssistTarget = null;

            int count = Physics.OverlapSphereNonAlloc(transform.position, range, aimAssistBuffer);
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = aimAssistBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                RemoteMonsterController monster = hit.GetComponentInParent<RemoteMonsterController>();
                if (monster == null || monster.IsDead)
                {
                    continue;
                }

                float distanceSqr = (monster.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    aimAssistTarget = monster;
                }
            }

            return aimAssistTarget;
        }

        /// <summary>
        /// Attack Layer의 Attack1/Attack2 상태는 무기 타입별 클립(예: SingleSword/THS/MagicWand/Spear의
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
        /// 공격 길이 표는 로컬/원격 플레이어가 공유하는 CombatTimings에 있다. 등록되지 않은 WeaponType이면 기본값(16프레임/30fps)으로 대체한다.
        /// </summary>
        private float GetStageDuration(int stage)
        {
            WeaponType weaponType = playerCharacterModel != null ? playerCharacterModel.CurrentWeaponType : WeaponType.None;

            if (CombatTimings.TryGetAttackDuration((int)weaponType, stage, maxComboStage, out float duration))
            {
                return duration;
            }

            DebugLogManager.GenerateErrorMessage<PlayerAttackController>($"WeaponType '{weaponType}'에 대응하는 공격 타이밍이 CombatTimings에 없습니다 - 기본 공격 길이를 사용합니다.");
            return duration;
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
            aimAssistTarget = null;
            wandAttackBuffered = false;

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

            if (!TryFindNearestTarget(hitCount, out long targetId, out bool isMonster))
            {
                return;
            }

            if (isMonster)
            {
                GameServerConnectManager.Instance?.SendMonsterAttack(targetId);

                if (RemoteMonsterManager.Instance != null && RemoteMonsterManager.Instance.TryGetRemoteMonster(targetId, out RemoteMonsterController targetMonster))
                {
                    MonsterTargeted?.Invoke(targetMonster);
                }
            }
            else
            {
                GameServerConnectManager.Instance?.SendAttack(targetId);
            }
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
        /// PlaySwingEffect와 같은 이유로 대상 유무와 무관하게(허공 스윙 포함) 콤보 타수마다 독립적으로 호출한다 -
        /// 근처 다른 플레이어가 내 공격 모션을 볼 수 있어야 하기 때문이다. RemoteCharacterController.PlayAttackAnimation 참고.
        /// </summary>
        private void BroadcastAttackAnimation()
        {
            WeaponType weaponType = playerCharacterModel != null ? playerCharacterModel.CurrentWeaponType : WeaponType.None;
            GameServerConnectManager.Instance?.SendAttackAnimation(comboStage, weaponType);
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

            // 완드의 명중 이펙트는 투사체가 대상에 도착하는 순간 ProjectileVfxManager가 재생한다(서버 판정이 더 일찍 와도 여기서 미리 터뜨리지 않는다).
            if (!IsWand)
            {
                WeaponVfxManager.Instance?.PlayImpactEffect(playerCharacterModel.CurrentWeaponType, ApplyEffectHeight(target.transform.position), target.transform.rotation, effectScale);
            }

            StartHitStop();
        }

        /// <summary>
        /// Game_MonsterDamageBroadcast는 전원에게 오지만, 여기서는 "내가 명중시킨" 경우만 처리해 몬스터 위치에
        /// 임팩트 이펙트를 재생한다(HandleDamageReceived의 몬스터 버전). RemainingHp/사망 여부는 RemoteMonsterManager가
        /// 직접 처리하므로 여기서는 이펙트 재생만 담당한다.
        /// </summary>
        private void HandleMonsterDamaged(GameMonsterDamageBroadcastPacket packet)
        {
            if (playerCharacterModel == null || SaveDataManager.Instance == null)
            {
                return;
            }

            if (packet.AttackerId != SaveDataManager.Instance.SelectedCharacterId)
            {
                return;
            }

            if (RemoteMonsterManager.Instance == null || !RemoteMonsterManager.Instance.TryGetRemoteMonster(packet.MonsterId, out RemoteMonsterController target))
            {
                return;
            }

            // 완드의 명중 이펙트는 투사체가 도착하는 순간 ProjectileVfxManager가 재생한다(HandleDamageReceived와 같은 이유).
            if (!IsWand)
            {
                WeaponVfxManager.Instance?.PlayImpactEffect(playerCharacterModel.CurrentWeaponType, ApplyEffectHeight(target.transform.position), target.transform.rotation, effectScale);
            }

            StartHitStop();
        }

        /// <summary>
        /// 내 공격이 명중했을 때 공격 애니메이션을 hitStopSeconds만큼 멈춰 타격감을 준다(히트스톱). Time.timeScale을 건드리지 않고
        /// 이 캐릭터의 애니메이터만 멈추므로 서버와 맞춰 돌아가는 보간/쿨다운/네트워크 시각에는 영향이 없다.
        /// 멈춘 시간만큼 애니메이션이 로직 타이머보다 살짝 늦어지지만 0.05초 안팎이라 눈에 띄지 않는다.
        /// </summary>
        private void StartHitStop()
        {
            if (hitStopSeconds <= 0f || animator == null || hitStopRoutine != null)
            {
                return;
            }

            hitStopRoutine = StartCoroutine(HitStopRoutine());
        }

        private System.Collections.IEnumerator HitStopRoutine()
        {
            float previousSpeed = animator.speed;
            animator.speed = 0f;

            // 실제 시간으로 기다린다(타임스케일과 무관하게 같은 길이로 멈추도록).
            yield return new WaitForSecondsRealtime(hitStopSeconds);

            if (animator != null)
            {
                animator.speed = previousSpeed;
            }

            hitStopRoutine = null;
        }

        // 히트스톱 도중 비활성화/해제되면 멈춘 채 남지 않게 속도를 1로 되돌리고 코루틴 상태를 지운다.
        private void EndHitStop()
        {
            if (hitStopRoutine == null)
            {
                return;
            }

            StopCoroutine(hitStopRoutine);
            hitStopRoutine = null;

            if (animator != null)
            {
                animator.speed = 1f;
            }
        }

        /// <summary>
        /// 다른 플레이어의 공격 모션 알림(Game_AttackAnimationBroadcast)을 받아 해당 원격 캐릭터에 재생시킨다.
        /// 본인의 공격은 로컬에서 즉시 재생하므로 이 이벤트로 오지 않는다.
        /// </summary>
        private void HandleAttackAnimationReceived(GameAttackAnimationBroadcastPacket packet)
        {
            if (RemotePlayerManager.Instance != null && RemotePlayerManager.Instance.TryGetRemotePlayer(packet.AttackerId, out RemoteCharacterController attacker))
            {
                attacker.PlayAttackAnimation(packet.ComboStage, (WeaponType)packet.WeaponType, (AttackTargetKind)packet.TargetType, packet.TargetId);
            }
        }

        /// <summary>
        /// 판정 범위 안의 원격 플레이어/몬스터를 통틀어 가장 가까운 대상 하나를 찾는다. 두 타입은 서로 다른
        /// OpCode(Game_AttackRequest/Game_MonsterAttackRequest)로 공격 요청을 보내야 하므로, 대상 종류(isMonster)도
        /// 함께 반환한다.
        /// </summary>
        private bool TryFindNearestTarget(int hitCount, out long targetId, out bool isMonster)
        {
            targetId = 0;
            isMonster = false;
            bool found = false;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                RemoteCharacterController remotePlayer = hit.GetComponentInParent<RemoteCharacterController>();
                if (remotePlayer != null)
                {
                    float distanceSqr = (remotePlayer.transform.position - transform.position).sqrMagnitude;
                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        targetId = remotePlayer.PlayerId;
                        isMonster = false;
                        found = true;
                    }
                    continue;
                }

                // 사망 연출 중인 몬스터는 대상에서 뺀다(콜라이더는 PlayDeath에서 꺼지지만, 같은 프레임에 걸린 경우까지 막는다).
                RemoteMonsterController remoteMonster = hit.GetComponentInParent<RemoteMonsterController>();
                if (remoteMonster != null && !remoteMonster.IsDead)
                {
                    float distanceSqr = (remoteMonster.transform.position - transform.position).sqrMagnitude;
                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        targetId = remoteMonster.MonsterId;
                        isMonster = true;
                        found = true;
                    }
                }
            }

            return found;
        }
    }
}
