using Incheol.Utils;
using Incheol.View.UI;
using UnityEngine;

[RequireComponent(typeof(HealthComponent), typeof(CombatStatComponent), typeof(EquipmentController))]
public class PlayerCharacterModel : MonoBehaviour
{
    #region Variable
    [Header("유저 캐릭터 이름표 UI")]
    [SerializeField] private UI_NameLabel nameLabel;

    [Header("무기")]
    [Tooltip("현재 장착된 무기 타입(전투 로직/공격 이펙트 조회 기준). 실제로 어떤 메시가 보이는지는 " +
        "EquipmentController가 equipVisualName을 기준으로 독립적으로 처리한다.")]
    [SerializeField] private WeaponType currentWeaponType = WeaponType.OneHanded;

    [Tooltip("인벤토리 연동 전, 기본으로 장착할 무기 메시 오브젝트 이름(rightArmEqiupment 하위, 기본 OHS01_Stick).")]
    [SerializeField] private string defaultWeaponVisualName = "OHS01_Stick";

    // Awake 시점의 currentWeaponType/defaultWeaponVisualName 조합. UnequipItem(Weapon)이 맨손이 아니라
    // 이 기본 무기로 되돌리는 기준이 된다(currentWeaponType은 EquipWeapon 호출마다 덮어써지므로 별도 캐싱이 필요하다).
    private WeaponType defaultWeaponType;

    private EquipmentController equipmentController;

    [Header("피격 연출")]
    [Tooltip("피격 모션(Attack Layer의 GetHit01/02, 상체 마스크)을 재생하는 시간(초). 모션 클립 길이(0.47초)보다 짧게 잡아 연속으로 맞아도 둔해 보이지 않게 한다.")]
    [SerializeField, Min(0.05f)] private float hitReactionDuration = 0.35f;

    [Tooltip("피격 모션이 끝난 뒤 Attack Layer 가중치를 0으로 서서히 내리는 시간(초). 0이면 즉시 끊겨 상체가 튄다.")]
    [SerializeField, Min(0f)] private float hitReactionFadeOutSeconds = 0.1f;

    // 캐릭터의 Animator와 Attack Layer 번호. Awake에서 한 번 찾아 둔다(피격마다 GetComponent/레이어 검색을 하지 않는다).
    private Animator characterAnimator;
    private int attackLayerIndex = -1;

    private enum HitReactionPhase
    {
        None,
        Playing,    // 피격 모션 재생 중(Attack Layer 가중치 1)
        FadingOut   // 모션이 끝나 가중치를 0으로 내리는 중
    }

    private HitReactionPhase hitReactionPhase = HitReactionPhase.None;
    private float hitReactionPhaseStartTime;
    private int nextHitVariant = 1;

    /// <summary>
    /// 세이브 데이터(UserSaveData.userExp)로부터 GameSceneController가 채워주는 경험치 런타임 상태.
    /// ApplyExp로 초기화된 뒤에는 GainExp로 갱신된다. 체력/공격력/방어력은 각각 HealthComponent/CombatStatComponent가 전담한다.
    /// UI_GameSceneView는 이 값을 계속 폴링해 슬라이더 연출에 사용한다.
    /// </summary>
    private int currentExp;
    private int expToNextLevel;
    private int level = 1;

    /// <summary>
    /// 세이브 데이터(UserSaveData.gold)로부터 ApplyUserSaveData가 채워주는 골드 런타임 상태.
    /// 이후 처치 보상(Game_LootBroadcast)마다 ApplyGoldGain으로 갱신된다.
    /// </summary>
    private long gold;

    // 스폰 시점의 원본 스탯(str/agi/intel, DB에 저장된 기본값 - 레벨 성장 보너스는 들어 있지 않다). 레벨이 바뀔 때마다
    // 이 값에 StatGrowth 보너스를 더해 공격력/방어력을 다시 계산하고, 인벤토리 스탯 패널 표시용으로도 쓴다.
    private UserStats cachedUserStats;
    private HealthComponent healthComponent;
    private CombatStatComponent combatStatComponent;

    public string Nickname => nameLabel != null ? nameLabel.Nickname : string.Empty;
    public WeaponType CurrentWeaponType => currentWeaponType;
    public int Level => level;
    public int MaxHp => healthComponent.MaxHp;
    public int CurrentHp => healthComponent.CurrentHp;
    public int CurrentExp => currentExp;
    public long Gold => gold;

    /// <summary>다음 레벨까지 필요한 경험치(경험치 바의 maxValue). 만렙이면 0.</summary>
    public int ExpToNextLevel => expToNextLevel;
    public int AttackPower => combatStatComponent.AttackPower;
    public int Defense => combatStatComponent.Defense;

    /// <summary>
    /// 인벤토리 스탯 패널에 표시할 능력치(str/agi/intel). ApplyUserSaveData가 캐싱해둔 원본 스탯에 현재 레벨의 성장 보너스
    /// (StatGrowth)를 더한 값이라, 공격력/방어력 계산에 실제로 쓰이는 값과 같다. 호출할 때마다 새 객체를 만들므로
    /// 매 프레임 호출하지 말고 UI를 갱신할 때만 쓴다. 아직 세이브 데이터가 적용되지 않았으면 null.
    /// </summary>
    public UserStats Stats
    {
        get
        {
            if (cachedUserStats == null)
            {
                return null;
            }

            int growth = StatGrowth.BonusAtLevel(level);
            return new UserStats
            {
                str = cachedUserStats.str + growth,
                agi = cachedUserStats.agi + growth,
                intel = cachedUserStats.intel + growth
            };
        }
    }
    #endregion

    #region LifeCycle
    private void Awake()
    {
        healthComponent = GetComponent<HealthComponent>();
        combatStatComponent = GetComponent<CombatStatComponent>();
        equipmentController = GetComponent<EquipmentController>();

        characterAnimator = GetComponent<Animator>();
        if (characterAnimator != null)
        {
            attackLayerIndex = characterAnimator.GetLayerIndex(AttackLayerName);
        }

        if (nameLabel == null)
        {
            nameLabel = GetComponentInChildren<UI_NameLabel>(true);
        }

        if (healthComponent == null || combatStatComponent == null || equipmentController == null)
        {
            DebugLogManager.GenerateErrorMessage<PlayerCharacterModel>("HealthComponent/CombatStatComponent/EquipmentController가 없어 캐릭터 초기화가 완전하지 않습니다.");
        }

        defaultWeaponType = currentWeaponType;
        EquipWeapon(currentWeaponType, defaultWeaponVisualName);
    }

    private void OnEnable()
    {
        // 로컬/원격 플레이어 모두 서버가 알린 피격(ApplyServerHp wasHit=true)이 HealthComponent.OnDamaged로 오므로
        // 여기 한 곳에서 피격 모션을 재생하면 다른 플레이어 화면에서도 똑같이 보인다.
        if (healthComponent != null)
        {
            healthComponent.OnDamaged += PlayHitReaction;
        }
    }

    private void OnDisable()
    {
        if (healthComponent != null)
        {
            healthComponent.OnDamaged -= PlayHitReaction;
        }

        CancelHitReaction();
    }

    private void Update()
    {
        UpdateHitReaction();
    }
    #endregion

    #region Method
    /// <summary>
    /// 캐릭터 머리 위 NameLabel에 닉네임을 설정한다.
    /// </summary>
    public void SetNickname(string nickname)
    {
        if (nameLabel == null)
        {
            nameLabel = GetComponentInChildren<UI_NameLabel>(true);
        }

        if (nameLabel != null)
        {
            nameLabel.SetNickname(nickname);
        }
    }
    /// <summary>
    /// weaponType(전투 로직/공격 이펙트 조회 기준)과 visualName(실제로 표시할 메시 오브젝트 이름)을 함께 반영한다.
    /// visualName을 생략하면 defaultWeaponVisualName을 사용한다. 실제 메시 전환은 EquipmentController가
    /// rightArmEqiupment/leftArmEqiupment 하위에서 이름으로 찾아 처리한다(CharacterCustomModel의 헤어/눈/입
    /// 교체와 같은 SetActive 토글 메커니즘이지만, 인덱스가 아니라 이름 기반이라 프리팹에 새 무기 메시가
    /// 추가되어도 코드 수정 없이 바로 장착 대상이 된다).
    /// </summary>
    public void EquipWeapon(WeaponType weaponType, string visualName = null)
    {
        currentWeaponType = weaponType;
        equipmentController.Equip(EquipmentSlotType.Weapon, string.IsNullOrEmpty(visualName) ? defaultWeaponVisualName : visualName);
        SyncWeaponAnimation();
    }

    // Base Layer의 Idle/Move/Jump/Die/Dash/BackDash는 공격(Attack Layer)과 같은 방식으로 WeaponIndex(WeaponType 값)를 임계값으로 하는
    // BlendTree라, 무기를 바꾸는 순간 이 값을 맞춰야 그 무기의 대기/이동/대쉬/사망 모션이 재생된다. 공격 때만 값을 맞추면
    // (PlayerAttackController.SyncWeaponIndex) 공격하기 전까지 평소 모션이 맨손 모션으로 남는다. 로컬/원격 플레이어와 로비 미리보기가
    // 모두 이 메서드(EquipWeapon)를 거치므로 한 곳에서 처리한다. 값이 임계값과 정확히 일치해야 블렌딩 없이 한 클립만 재생된다.
    private static readonly int WeaponIndexHash = Animator.StringToHash("WeaponIndex");

    private void SyncWeaponAnimation()
    {
        if (TryGetComponent(out Animator animator) && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(WeaponIndexHash, (float)currentWeaponType);
        }
    }

    /// <summary>
    /// 인벤토리 아이템 하나를 장착한다. 슬롯 종류에 따라 EquipmentController에 필요한 정보(무기는 weaponType까지)를
    /// 골라 전달하는 단일 진입점이다 - 추후 인벤토리 UI에서 장비를 교체할 때 슬롯별로 다른 메서드를 호출할 필요 없이
    /// 이 함수 하나만 호출하면 된다.
    /// </summary>
    public void EquipItem(ItemData itemData)
    {
        if (itemData == null || itemData.itemType != ItemType.Eqiupment || itemData.equipSlotType == EquipmentSlotType.None)
        {
            return;
        }

        if (itemData.equipSlotType == EquipmentSlotType.Weapon)
        {
            EquipWeapon(itemData.weaponType, itemData.equipVisualName);
            return;
        }

        equipmentController.Equip(itemData.equipSlotType, itemData.equipVisualName);
    }

    /// <summary>
    /// 장비 슬롯 하나를 해제한다. EquipItem과 대칭되는 단일 진입점 - 인벤토리 UI는 슬롯 종류별로 분기할 필요 없이
    /// 이 함수만 호출하면 된다. Weapon 슬롯은 맨손이 아니라 defaultWeaponType/defaultWeaponVisualName(기본 무기)으로
    /// 되돌아간다 - 이 캐릭터는 항상 최소한 기본 무기를 들고 있는 것이 정상 상태이기 때문이다.
    /// </summary>
    public void UnequipItem(EquipmentSlotType slot)
    {
        if (slot == EquipmentSlotType.Weapon)
        {
            EquipWeapon(defaultWeaponType, defaultWeaponVisualName);
            return;
        }

        equipmentController.Unequip(slot);
    }

    /// <summary>
    /// 현재 장착 중인 장비 전체의 공격력/방어력 보너스 합계를 CombatStatComponent에 반영한다. GameSceneManager가
    /// 장착/해제가 성공할 때마다(RecalculateEquipmentStats) 새로 합산한 값을 넘겨 호출한다.
    /// </summary>
    public void SetEquipmentBonus(int bonusAttackPower, int bonusDefense)
    {
        combatStatComponent.SetEquipmentBonus(bonusAttackPower, bonusDefense);
    }

    /// <summary>
    /// 세이브 데이터의 체력값을 캐릭터에 반영한다(스폰 시 최초 1회). currentHp가 maxHp를 넘거나
    /// 음수가 되지 않도록 보정한다. 이후 전투 중 체력 변화는 서버 결과를 ApplyServerHp로 반영한다.
    /// </summary>
    public void ApplyHealth(int newMaxHp, int newCurrentHp)
    {
        healthComponent.ApplyHealth(newMaxHp, newCurrentHp);
    }

    /// <summary>
    /// 캐릭터 레벨을 설정한다. 1 미만으로는 내려가지 않는다.
    /// 레벨이 오르면 레벨 성장 보너스(StatGrowth)가 달라지므로 공격력/방어력도 곧바로 다시 계산한다(서버가 같은 시점에 같은 공식으로
    /// 자기 쪽 값을 갱신하고 있다). 최대 체력은 서버가 보내는 값(Game_PlayerHpBroadcast)을 ApplyServerHp로 받아 반영한다.
    /// 원격 플레이어처럼 원본 스탯이 없는 캐릭터(ApplyRemoteCombatState로 서버가 계산한 값을 받는 경우)는 다시 계산하지 않는다.
    /// </summary>
    public void ApplyLevel(int newLevel)
    {
        level = Mathf.Max(1, newLevel);

        if (cachedUserStats != null)
        {
            combatStatComponent.ApplyFromUserStats(cachedUserStats, level);
        }
    }

    /// <summary>
    /// GameSceneManager가 캐릭터 스폰 직후(외형 적용과 함께) 한 번에 호출하는 진입점.
    /// 닉네임/레벨/체력(스탯+레벨 기반 임시 공식)/공격력·방어력/경험치를 세이브 데이터(DB 영속값)로 초기화한다.
    /// </summary>
    public void ApplyUserSaveData(UserSaveData saveData)
    {
        if (saveData == null)
        {
            return;
        }

        cachedUserStats = saveData.userStats;

        SetNickname(saveData.nickname);
        ApplyLevel(saveData.level);
        healthComponent.ApplyFromUserStats(saveData.userStats, saveData.level);
        combatStatComponent.ApplyFromUserStats(saveData.userStats, saveData.level);
        ApplyExp(saveData.exp);
        ApplyGold(saveData.gold);
    }

    /// <summary>
    /// 원격 플레이어 스폰 시(RemotePlayerManager.HandlePlayerJoined) 서버가 중계한 GamePlayerInfo의
    /// 전투 스냅샷을 그대로 반영한다. 원격 캐릭터는 UserSaveData를 직접 조회할 수 없으므로,
    /// 이미 계산되어 넘어온 값을 그대로 HealthComponent/CombatStatComponent에 채운다.
    /// </summary>
    public void ApplyRemoteCombatState(int maxHp, int currentHp, int attackPower, int defense)
    {
        healthComponent.ApplyHealth(maxHp, currentHp);
        combatStatComponent.ApplyRaw(attackPower, defense);
    }

    public bool IsDead => healthComponent.IsDead;

    /// <summary>
    /// GameServer가 계산한 체력을 반영한다(로컬/원격 공용). 피격 브로드캐스트(Game_DamageBroadcast/
    /// Game_MonsterAttackBroadcast)는 wasHit=true, 레벨업/부활은 false로 호출한다. 이 호출로 사망하거나
    /// 부활하면 사망/기상 애니메이션도 함께 전환한다.
    /// </summary>
    public void ApplyServerHp(int currentHp, int maxHp, bool wasHit)
    {
        bool wasDead = healthComponent.IsDead;
        healthComponent.ApplyServerHp(currentHp, maxHp, wasHit);

        if (!wasDead && healthComponent.IsDead)
        {
            ClearActionAnimations();
            PlayStateAnimation(DieStateHash);
        }
        else if (wasDead && !healthComponent.IsDead)
        {
            PlayStateAnimation(IdleStateHash);
        }
    }

    // BasicCharacterStance(Base Layer)의 Die 상태에는 들어오고 나가는 전이가 없어서, 파라미터 대신 상태를 직접
    // 재생한다 - 한 번 Die로 들어가면 IsIdle/IsMove 값과 상관없이 부활 때 Idle을 다시 재생할 때까지 쓰러져 있다.
    private static readonly int DieStateHash = Animator.StringToHash("Die");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");

    // 사망 직전에 진행 중이던 공격/대쉬 모션을 걷어낸다. Attack Layer는 weight가 1로 남으면 Die 위에 덮어 그려지고,
    // Base Layer의 AnyState -> Dash(IsDash) 전이는 IsDash가 켜진 채면 Die 재생 직후 다시 Dash로 끌고 간다.
    // 사망하면 PlayerAttackController/PlayerMoveController가 꺼져 스스로 정리하지 못하므로 여기서 대신 정리한다.
    private const string AttackLayerName = "Attack Layer";
    private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
    private static readonly int IsDashHash = Animator.StringToHash("IsDash");
    private static readonly int IsBackDashHash = Animator.StringToHash("IsBackDash");

    private void ClearActionAnimations()
    {
        if (!TryGetComponent(out Animator animator))
        {
            return;
        }

        int attackLayerIndex = animator.GetLayerIndex(AttackLayerName);
        if (attackLayerIndex >= 0)
        {
            animator.SetInteger(ComboIndexHash, 0);
            animator.SetLayerWeight(attackLayerIndex, 0f);
        }

        animator.SetBool(IsDashHash, false);
        animator.SetBool(IsBackDashHash, false);

        // 피격 모션도 걷어낸다(HitIndex가 남으면 GetHit 상태가 Die 위에 덮이고 루프 클립이라 계속 반복된다). 가중치는 위에서 0이 됐다.
        animator.SetInteger(HitIndexHash, 0);
        hitReactionPhase = HitReactionPhase.None;
    }

    // 피격 모션은 Attack Layer(상체 마스크)의 GetHit01/GetHit02 상태다. AnyState에서 HitIndex 값(1/2)으로 들어가고 HitIndex가 0이
    // 되면 AttackLayerIdle로 나가는데, 클립이 루프라 HitIndex를 0으로 되돌리지 않으면 영원히 반복된다. Attack Layer는 평소
    // 가중치 0이라 재생하는 동안만 1로 올렸다가 끝나면 서서히 내린다.
    private static readonly int HitIndexHash = Animator.StringToHash("HitIndex");

    /// <summary>
    /// 피격 모션을 재생한다. HealthComponent.OnDamaged(서버가 알린 피격)에 연결돼 있다. 다음 경우에는 재생하지 않는다 -
    ///  · 사망(이 피격으로 죽었으면 OnDamaged가 사망 처리보다 먼저 오지만 이미 IsDead라 걸러진다): 사망 모션이 대신한다.
    ///  · 공격 콤보 중(ComboIndex > 0): 피격 모션이 콤보 상태를 끊으면 이후 AttackLayerIdle이 남은 ComboIndex를 보고 공격
    ///    모션을 저절로 다시 시작한다. 공격 중에는 끊기지 않는 방식이라 오히려 조작감이 낫다.
    /// 이미 재생 중에 또 맞으면 반대 모션(GetHit01 <-> GetHit02)으로 바꿔 다시 재생한다.
    /// </summary>
    private void PlayHitReaction()
    {
        if (healthComponent.IsDead || characterAnimator == null || attackLayerIndex < 0)
        {
            return;
        }

        if (characterAnimator.GetInteger(ComboIndexHash) > 0)
        {
            return;
        }

        characterAnimator.SetInteger(HitIndexHash, nextHitVariant);
        nextHitVariant = nextHitVariant == 1 ? 2 : 1;
        characterAnimator.SetLayerWeight(attackLayerIndex, 1f);

        hitReactionPhase = HitReactionPhase.Playing;
        hitReactionPhaseStartTime = Time.time;
    }

    /// <summary>
    /// 피격 모션이 재생 중이거나 사라지는 중인지. RemoteCharacterController가 Attack Layer 가중치를 자기 공격 모션 시간표대로
    /// 0으로 되돌리는데, 피격 모션 동안에는 그걸 건너뛰어야 다른 플레이어 화면에서도 피격 모션이 보인다.
    /// </summary>
    public bool IsHitReactionPlaying => hitReactionPhase != HitReactionPhase.None;

    /// <summary>
    /// 피격 모션을 즉시 끝낸다. 공격이 시작될 때(PlayerAttackController.StartCombo) 불러, 피격 모션이 끝나기를 기다리느라 공격이
    /// 늦어지지 않게 한다. 공격이 Attack Layer를 쓰고 있으면(ComboIndex > 0) 가중치는 공격 쪽이 관리하므로 건드리지 않는다.
    /// </summary>
    public void CancelHitReaction()
    {
        if (hitReactionPhase == HitReactionPhase.None || characterAnimator == null)
        {
            hitReactionPhase = HitReactionPhase.None;
            return;
        }

        hitReactionPhase = HitReactionPhase.None;
        characterAnimator.SetInteger(HitIndexHash, 0);

        if (attackLayerIndex >= 0 && characterAnimator.GetInteger(ComboIndexHash) == 0)
        {
            characterAnimator.SetLayerWeight(attackLayerIndex, 0f);
        }
    }

    private void UpdateHitReaction()
    {
        if (hitReactionPhase == HitReactionPhase.None || characterAnimator == null)
        {
            return;
        }

        // 피격 모션 도중 공격이 시작돼 Attack Layer를 가져갔으면 더 이상 건드리지 않는다.
        if (characterAnimator.GetInteger(ComboIndexHash) > 0)
        {
            hitReactionPhase = HitReactionPhase.None;
            return;
        }

        float elapsed = Time.time - hitReactionPhaseStartTime;

        if (hitReactionPhase == HitReactionPhase.Playing)
        {
            if (elapsed < hitReactionDuration)
            {
                return;
            }

            // 모션이 끝났다: HitIndex를 0으로 돌려 GetHit -> AttackLayerIdle로 나가게 하고, 가중치를 서서히 내린다.
            characterAnimator.SetInteger(HitIndexHash, 0);
            hitReactionPhase = HitReactionPhase.FadingOut;
            hitReactionPhaseStartTime = Time.time;
            return;
        }

        float fade = hitReactionFadeOutSeconds > 0f ? Mathf.Clamp01(elapsed / hitReactionFadeOutSeconds) : 1f;
        characterAnimator.SetLayerWeight(attackLayerIndex, 1f - fade);

        if (fade >= 1f)
        {
            hitReactionPhase = HitReactionPhase.None;
        }
    }

    private void PlayStateAnimation(int stateHash)
    {
        if (TryGetComponent(out Animator animator))
        {
            animator.Play(stateHash, 0, 0f);
        }
    }

    /// <summary>
    /// 세이브 데이터의 경험치값을 캐릭터에 반영한다(스폰 시 최초 1회). 이후 경험치 획득은 ApplyExpGain을 사용한다.
    /// </summary>
    public void ApplyExp(int exp)
    {
        currentExp = Mathf.Max(0, exp);
        expToNextLevel = ExpTable.GetRequiredExp(level);
    }

    /// <summary>
    /// GameSceneManager가 Game_ExpGainBroadcast(서버 권위)를 받으면 호출한다. 델타를 누적하는 대신
    /// 서버가 계산한 최종 상태(총 경험치/레벨/다음 레벨까지 필요치)로 그대로 덮어쓴다 - 패킷 유실이 있어도
    /// 다음 패킷에서 자연히 복구된다(S2CMonsterDamageBroadcast.RemainingHp와 같은 이유).
    /// 레벨업에 따른 최대 체력 증가/회복은 서버가 계산해 Game_PlayerHpBroadcast로 따로 보내므로 여기서는 하지 않는다.
    /// </summary>
    public void ApplyExpGain(int totalExp, int newLevel, int newExpToNextLevel)
    {
        currentExp = Mathf.Max(0, totalExp);
        expToNextLevel = Mathf.Max(0, newExpToNextLevel);

        if (newLevel != level)
        {
            ApplyLevel(newLevel);
        }
    }

    /// <summary>
    /// 세이브 데이터의 골드값을 캐릭터에 반영한다(스폰 시 최초 1회). 이후 골드 획득은 ApplyGoldGain을 사용한다.
    /// </summary>
    public void ApplyGold(long newGold)
    {
        gold = System.Math.Max(0L, newGold);
    }

    /// <summary>
    /// GameSceneManager가 Game_LootBroadcast(서버 권위)를 받으면 호출한다. ApplyExpGain과 달리 서버가
    /// 최종 총액이 아니라 이번에 획득한 델타만 보내므로(S2CLootBroadcast.GoldGained), 여기서는 그대로 누적한다.
    /// </summary>
    public void ApplyGoldGain(int goldGained)
    {
        gold += goldGained;
    }

    #endregion
}
