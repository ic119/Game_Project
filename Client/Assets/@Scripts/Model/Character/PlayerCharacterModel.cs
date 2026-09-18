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

    private EquipmentController equipmentController;

    /// <summary>
    /// 세이브 데이터(UserSaveData.userExp)로부터 GameSceneController가 채워주는 경험치 런타임 상태.
    /// ApplyExp로 초기화된 뒤에는 GainExp로 갱신된다. 체력/공격력/방어력은 각각 HealthComponent/CombatStatComponent가 전담한다.
    /// UI_GameSceneView는 이 값을 계속 폴링해 슬라이더 연출에 사용한다.
    /// </summary>
    private int currentExp;
    private int expToNextLevel;
    private int level = 1;

    // 레벨업 시 MaxHp를 재계산(HealthComponent.ApplyFromUserStats)하려면 원본 스탯이 필요해 스폰 시점에 캐싱해둔다.
    private UserStats cachedUserStats;
    private HealthComponent healthComponent;
    private CombatStatComponent combatStatComponent;

    public string Nickname => nameLabel != null ? nameLabel.Nickname : string.Empty;
    public WeaponType CurrentWeaponType => currentWeaponType;
    public int Level => level;
    public int MaxHp => healthComponent.MaxHp;
    public int CurrentHp => healthComponent.CurrentHp;
    public int CurrentExp => currentExp;

    /// <summary>다음 레벨까지 필요한 경험치(경험치 바의 maxValue). 만렙이면 0.</summary>
    public int ExpToNextLevel => expToNextLevel;
    public int AttackPower => combatStatComponent.AttackPower;
    public int Defense => combatStatComponent.Defense;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        healthComponent = GetComponent<HealthComponent>();
        combatStatComponent = GetComponent<CombatStatComponent>();
        equipmentController = GetComponent<EquipmentController>();

        if (nameLabel == null)
        {
            nameLabel = GetComponentInChildren<UI_NameLabel>(true);
        }

        if (healthComponent == null || combatStatComponent == null || equipmentController == null)
        {
            DebugLogManager.GenerateErrorMessage<PlayerCharacterModel>("HealthComponent/CombatStatComponent/EquipmentController가 없어 캐릭터 초기화가 완전하지 않습니다.");
        }

        EquipWeapon(currentWeaponType, defaultWeaponVisualName);
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
    /// 세이브 데이터의 체력값을 캐릭터에 반영한다(스폰 시 최초 1회). currentHp가 maxHp를 넘거나
    /// 음수가 되지 않도록 보정한다. 이후 전투 중 체력 변화는 HealthComponent.TakeDamage(IDamageable 구현)로 처리된다.
    /// </summary>
    public void ApplyHealth(int newMaxHp, int newCurrentHp)
    {
        healthComponent.ApplyHealth(newMaxHp, newCurrentHp);
    }

    /// <summary>
    /// 캐릭터 레벨을 설정한다. 1 미만으로는 내려가지 않는다.
    /// </summary>
    public void ApplyLevel(int newLevel)
    {
        level = Mathf.Max(1, newLevel);
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
        combatStatComponent.ApplyFromUserStats(saveData.userStats);
        ApplyExp(saveData.exp);
    }

    /// <summary>
    /// 세이브 데이터의 UserStats(str/agi/intel)로부터 공격력/방어력을 계산해 반영한다(스폰 시 최초 1회).
    /// </summary>
    public void ApplyCombatStat(UserStats userStats)
    {
        combatStatComponent.ApplyFromUserStats(userStats);
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

    /// <summary>
    /// IDamageable(HealthComponent)로의 패스스루. Game_DamageBroadcast를 받은 쪽(로컬/원격 공용)이
    /// PlayerCharacterModel만 알아도 데미지를 적용할 수 있도록 한다.
    /// </summary>
    public void TakeDamage(DamageInfo damageInfo)
    {
        healthComponent.TakeDamage(damageInfo);
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
    /// 레벨이 올랐다면 MaxHp도 새 레벨 기준으로 다시 계산한다(HealthComponent.ApplyFromUserStats).
    /// </summary>
    public void ApplyExpGain(int totalExp, int newLevel, int newExpToNextLevel)
    {
        currentExp = Mathf.Max(0, totalExp);
        expToNextLevel = Mathf.Max(0, newExpToNextLevel);

        if (newLevel != level)
        {
            ApplyLevel(newLevel);
            if (cachedUserStats != null)
            {
                healthComponent.ApplyFromUserStats(cachedUserStats, level);
            }
        }
    }

    #endregion
}
