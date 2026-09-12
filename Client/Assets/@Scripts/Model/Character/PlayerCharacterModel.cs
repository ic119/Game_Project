using Incheol.Utils;
using Incheol.View.UI;
using UnityEngine;

[RequireComponent(typeof(HealthComponent), typeof(CombatStatComponent))]
public class PlayerCharacterModel : MonoBehaviour
{
    #region Variable
    [Header("유저 캐릭터 이름표 UI")]
    [SerializeField] private UI_NameLabel nameLabel;

    [Header("유저 캐릭터 장비 컨테이너")]
    [SerializeField] private GameObject bodyEqiupment;
    [SerializeField] private GameObject backPackEqiupment;
    [SerializeField] private GameObject cloakEqiupment;
    [SerializeField] private GameObject leftArmEqiupment;
    [SerializeField] private GameObject rightArmEqiupment;

    [Header("무기")]
    [Tooltip("현재 장착된 무기 타입. rightArmEqiupment 하위에 무기별 메시가 이미 배치되어 있고 " +
        "전부 비활성 상태로 시작하므로, EquipWeapon이 타입에 맞는 오브젝트만 활성화한다.")]
    [SerializeField] private WeaponType currentWeaponType = WeaponType.OneHanded;

    /// <summary>
    /// WeaponType.OneHanded(한손무기류, 오브젝트 이름에 OH 접두사)에 대응하는 무기 오브젝트.
    /// rightArmEqiupment 하위, 기본값은 OHS03_Sword.
    /// TwoHanded/Shield/Wand/Spear는 아직 장착 대상 오브젝트가 지정되지 않았다.
    /// </summary>
    [Tooltip("WeaponType.OneHanded(한손무기류)에 대응하는 무기 오브젝트(rightArmEqiupment 하위, 기본 OHS03_Sword).")]
    [SerializeField] private GameObject singleSwordWeaponObject;

    /// <summary>
    /// 세이브 데이터(UserSaveData.userExp)로부터 GameSceneController가 채워주는 경험치 런타임 상태.
    /// ApplyExp로 초기화된 뒤에는 GainExp로 갱신된다. 체력/공격력/방어력은 각각 HealthComponent/CombatStatComponent가 전담한다.
    /// UI_GameSceneView는 이 값을 계속 폴링해 슬라이더 연출에 사용한다.
    /// </summary>
    private float currentExp;
    private HealthComponent healthComponent;
    private CombatStatComponent combatStatComponent;

    public string Nickname => nameLabel != null ? nameLabel.Nickname : string.Empty;
    public WeaponType CurrentWeaponType => currentWeaponType;
    public int MaxHp => healthComponent.MaxHp;
    public int CurrentHp => healthComponent.CurrentHp;
    public float CurrentExp => currentExp;
    public int AttackPower => combatStatComponent.AttackPower;
    public int Defense => combatStatComponent.Defense;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        healthComponent = GetComponent<HealthComponent>();
        combatStatComponent = GetComponent<CombatStatComponent>();

        if (nameLabel == null)
        {
            nameLabel = GetComponentInChildren<UI_NameLabel>(true);
        }

        if (healthComponent == null || combatStatComponent == null)
        {
            DebugLogManager.GenerateErrorMessage<PlayerCharacterModel>("HealthComponent/CombatStatComponent가 없어 체력/공격력 계산이 동작하지 않습니다.");
        }

        EquipWeapon(currentWeaponType);
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
    /// _weaponType에 해당하는 무기 오브젝트만 활성화하고 나머지는 비활성화한다.
    /// 무기 프리팹을 새로 생성/파괴하는 대신, rightArmEqiupment 하위에 이미 배치된
    /// 무기 메시들 중 하나를 켜고 끄는 방식이다(CharacterCustomModel의 헤어/눈/입 교체와 동일한 패턴).
    /// 현재는 OneHanded(한손무기류)만 실제 오브젝트가 연결되어 있다. TwoHanded/Shield/Wand/Spear는
    /// WeaponType에는 존재하지만 아직 대응하는 오브젝트 참조가 없어 장착해도 아무 것도 표시되지 않는다.
    /// </summary>
    public void EquipWeapon(WeaponType weaponType)
    {
        currentWeaponType = weaponType;

        SetWeaponActive(singleSwordWeaponObject, weaponType == WeaponType.OneHanded);
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
    /// 세이브 데이터의 UserStats(str/agi/intel)로부터 공격력/방어력을 계산해 반영한다(스폰 시 최초 1회).
    /// </summary>
    public void ApplyCombatStat(UserStats userStats)
    {
        combatStatComponent.ApplyFromUserStats(userStats);
    }

    /// <summary>
    /// 세이브 데이터의 경험치값을 캐릭터에 반영한다(스폰 시 최초 1회). 이후 경험치 획득은 GainExp를 사용한다.
    /// </summary>
    public void ApplyExp(float exp)
    {
        currentExp = Mathf.Max(0f, exp);
    }

    /// <summary>
    /// amount만큼 경험치를 더한다.
    /// </summary>
    public void GainExp(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentExp += amount;
    }

    private static void SetWeaponActive(GameObject weaponObject, bool isActive)
    {
        if (weaponObject == null)
        {
            return;
        }

        weaponObject.SetActive(isActive);
    }
    #endregion
}
