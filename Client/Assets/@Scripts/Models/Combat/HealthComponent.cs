using System;
using Incheol.Utils;
using UnityEngine;

/// <summary>
/// 체력/피격/사망을 전담하는 공용 컴포넌트. 특정 캐릭터 종류에 의존하지 않아
/// PlayerCharacterModel뿐 아니라 향후 추가될 Enemy 쪽도 그대로 재사용할 수 있다.
/// 플레이어 체력은 GameServer가 유일한 권위라, 피격/레벨업/부활로 인한 체력 변화는 직접 계산하지 않고
/// 서버가 보낸 결과를 ApplyServerHp로 그대로 반영한다.
/// UI 갱신은 기존 프로젝트 컨벤션(폴링)을 따르도록 CurrentHp/MaxHp를 그대로 노출하되,
/// 피격/사망 순간에만 반응하면 되는 연출(히트 리액션, 사망 처리)을 위해 이벤트도 함께 제공한다.
/// </summary>
public class HealthComponent : MonoBehaviour
{
    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;

    /// <summary>사망 상태에서 체력이 다시 생겼을 때(서버 부활) 발화된다.</summary>
    public event Action OnRevived;

    /// <summary>ApplyServerHp에 wasHit=true로 피격 결과가 들어올 때마다 발화된다(피격 연출 트리거용). ApplyHealth로인 초기화/회복에서는 발화되지 않는다.</summary>
    public event Action OnDamaged;

    /// <summary>서버가 보낸 체력이 살아 있는 상태에서 늘었을 때(물약/레벨업) 발화된다(회복 연출 트리거용). ApplyHealth로인 초기화에서는 발화되지 않는다.</summary>
    public event Action OnHealed;

    private int maxHp;
    private int currentHp;

    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public bool IsDead => currentHp <= 0;

    /// <summary>
    /// 세이브 데이터 등 외부 값으로 체력을 초기화한다(스폰 시 최초 1회).
    /// currentHp가 maxHp를 넘거나 음수가 되지 않도록 보정한다.
    /// 이후 전투 중 체력 변화는 서버 결과를 ApplyServerHp로 반영한다.
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
    /// agi에는 레벨 성장 보너스(StatGrowth)가 더해진다 - 서버(CombatStatCalculator.CalculateMaxHp)와 같은 공식이다.
    /// 이후 최대 체력은 서버가 보낸 값(ApplyServerHp)을 그대로 쓰므로 이 값은 스폰 직후 서버 응답을 받기 전까지의 표시값이다.
    /// </summary>
    public void ApplyFromUserStats(UserStats userStats, int level)
    {
        if (userStats == null)
        {
            return;
        }

        int agi = userStats.agi + StatGrowth.BonusAtLevel(level);
        int calculatedMaxHp = 100 + agi * 5 + Mathf.Max(0, level - 1) * 10;
        ApplyHealth(calculatedMaxHp, calculatedMaxHp);
    }

    /// <summary>
    /// GameServer가 계산한 체력(피격 결과의 RemainingHp, 레벨업/부활 후 체력)을 그대로 반영한다.
    /// wasHit이면 피격 연출용 OnDamaged를 발화하고, 살아 있다가 0이 되면 OnDied, 사망 상태에서 체력이 생기면
    /// OnRevived, 살아 있는 채로 체력이 늘면(물약/레벨업) OnHealed를 발화한다 - 사망/부활/회복 판단도 서버 값의 변화로만 한다.
    /// </summary>
    public void ApplyServerHp(int newCurrentHp, int newMaxHp, bool wasHit)
    {
        bool wasDead = IsDead;
        int previousHp = currentHp;

        maxHp = Mathf.Max(0, newMaxHp);
        currentHp = Mathf.Clamp(newCurrentHp, 0, maxHp);
        OnHealthChanged?.Invoke(currentHp, maxHp);

        if (wasHit)
        {
            OnDamaged?.Invoke();
        }

        if (!wasDead && IsDead)
        {
            OnDied?.Invoke();
        }
        else if (wasDead && !IsDead)
        {
            OnRevived?.Invoke();
        }
        else if (!wasDead && currentHp > previousHp)
        {
            OnHealed?.Invoke();
        }
    }
}
