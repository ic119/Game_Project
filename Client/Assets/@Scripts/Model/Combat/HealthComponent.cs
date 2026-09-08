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
    /// IDamageable 구현. damageInfo.Amount(방어력 적용 전 원본 데미지)에 자신의 defense를 적용해
    /// 최종 데미지만큼 체력을 깎는다. 이미 사망한 상태면 무시한다.
    /// </summary>
    //public void TakeDamage(DamageInfo damageInfo)
    //{
    //    if (IsDead)
    //    {
    //        return;
    //    }
    //
    //    int defense = combatStat != null ? combatStat.Defense : 0;
    //    int finalDamage = CombatCalculator.ApplyDefense(damageInfo.Amount, defense);
    //
    //    currentHp = Mathf.Clamp(currentHp - finalDamage, 0, maxHp);
    //    OnHealthChanged?.Invoke(currentHp, maxHp);
    //    OnDamaged?.Invoke();
    //
    //    if (currentHp <= 0)
    //    {
    //        OnDied?.Invoke();
    //    }
    //}
}
