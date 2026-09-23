using System;
using UnityEngine;

/// <summary>
/// 체력/피격/사망을 전담하는 공용 컴포넌트. 특정 캐릭터 종류에 의존하지 않아
/// PlayerCharacterModel뿐 아니라 향후 추가될 Enemy 쪽도 그대로 재사용할 수 있다.
/// 방어력은 같은 GameObject의 CombatStatComponent(있는 경우)에서 가져온다.
/// UI 갱신은 기존 프로젝트 컨벤션(폴링)을 따르도록 CurrentHp/MaxHp를 그대로 노출하되,
/// 피격/사망 순간에만 반응하면 되는 연출(히트 리액션, 사망 처리)을 위해 이벤트도 함께 제공한다.
/// </summary>
public class HealthComponent : MonoBehaviour, IDamageable
{
    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;

    /// <summary>사망 여부와 무관하게 TakeDamage로 공격을 받을 때마다 발화된다(피격 연출 트리거용). ApplyHealth로인 초기화/회복에서는 발화되지 않는다.</summary>
    public event Action OnDamaged;

    /// <summary>Heal로 체력을 회복할 때마다 발화된다(회복 연출 트리거용). ApplyHealth로인 초기화에서는 발화되지 않는다.</summary>
    public event Action OnHealed;

    private int maxHp;
    private int currentHp;
    private CombatStatComponent combatStat;

    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public bool IsDead => currentHp <= 0;

    private void Awake()
    {
        combatStat = GetComponent<CombatStatComponent>();
    }

    /// <summary>
    /// 세이브 데이터 등 외부 값으로 체력을 초기화한다(스폰 시 최초 1회).
    /// currentHp가 maxHp를 넘거나 음수가 되지 않도록 보정한다.
    /// 이후 전투 중 체력 변화는 TakeDamage를 사용한다.
    /// </summary>
    public void ApplyHealth(int newMaxHp, int newCurrentHp)
    {
        maxHp = Mathf.Max(0, newMaxHp);
        currentHp = Mathf.Clamp(newCurrentHp, 0, maxHp);

        OnHealthChanged?.Invoke(currentHp, maxHp);
    }

    /// <summary>
    /// UserStats(agi)와 레벨로부터 최대 체력을 계산해 만피로 초기화한다(스폰 시 최초 1회).
    /// agi 1당 체력 5, 레벨 1당 체력 10으로 잡은 임시 공식이며, CombatStatComponent.ApplyFromUserStats와
    /// 같은 성격의 임시값이다 - 실제 밸런스 기획이 정해지면 이 메서드 하나만 바꾸면 된다.
    /// </summary>
    public void ApplyFromUserStats(UserStats userStats, int level)
    {
        if (userStats == null)
        {
            return;
        }

        int calculatedMaxHp = 100 + userStats.agi * 5 + Mathf.Max(0, level - 1) * 10;
        ApplyHealth(calculatedMaxHp, calculatedMaxHp);
    }

    /// <summary>
    /// IDamageable 구현. damageInfo.Amount(방어력 적용 전 원본 데미지)에 자신의 defense를 적용해
    /// 최종 데미지만큼 체력을 깎는다. 이미 사망한 상태면 무시한다.
    /// </summary>
    public void TakeDamage(DamageInfo damageInfo)
    {
        if (IsDead)
        {
            return;
        }

        int defense = combatStat != null ? combatStat.Defense : 0;
        int finalDamage = CombatCalculator.ApplyDefense(damageInfo.Amount, defense);

        currentHp = Mathf.Clamp(currentHp - finalDamage, 0, maxHp);
        OnHealthChanged?.Invoke(currentHp, maxHp);
        OnDamaged?.Invoke();

        if (currentHp <= 0)
        {
            OnDied?.Invoke();
        }
    }

    /// <summary>
    /// 체력을 amount만큼 회복시킨다(포션 등 회복 아이템 사용 시 진입점). currentHp가 maxHp를 넘지 않도록
    /// 보정한다. 이미 사망한 상태면 무시한다 - 부활은 이 메서드의 책임이 아니라 별도 로직이어야 한다.
    /// 반환값은 실제로 currentHp가 변했는지(=효과가 있었는지) 여부다 - 사망 상태이거나 이미 만피라
    /// 아무 변화가 없었다면 false를 반환하며, 호출부(TryUseHealthPotion 등)는 이 값으로 아이템 소모 여부를
    /// 판단해 "효과 없는 사용"으로 아이템만 낭비되는 것을 막는다.
    /// </summary>
    public bool Heal(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return false;
        }

        int previousHp = currentHp;
        currentHp = Mathf.Clamp(currentHp + amount, 0, maxHp);

        if (currentHp == previousHp)
        {
            return false;
        }

        OnHealthChanged?.Invoke(currentHp, maxHp);
        OnHealed?.Invoke();
        return true;
    }

    /// <summary>
    /// 최대체력 대비 healPercent(%)만큼 회복시킨다(ItemData.healPercent를 그대로 전달받는 포션 사용 전용 진입점).
    /// 실제 회복량 계산은 CombatCalculator.CalculateHealAmount가 담당한다. 반환값은 Heal(amount)와 동일하게
    /// 실제로 회복이 일어났는지 여부다.
    /// </summary>
    public bool HealByPercent(int healPercent)
    {
        return Heal(CombatCalculator.CalculateHealAmount(maxHp, healPercent));
    }
}
