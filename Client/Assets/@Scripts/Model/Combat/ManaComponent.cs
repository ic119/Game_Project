using System;
using Incheol.Utils;
using UnityEngine;

/// <summary>
/// 마나를 전담하는 컴포넌트. HealthComponent와 같은 구조다 - 플레이어 마나는 GameServer가 유일한 권위라, 자연 회복/레벨업/부활/스킬
/// 소모로 인한 마나 변화는 직접 계산하지 않고 서버가 보낸 결과를 ApplyServerMp로 그대로 반영한다.
/// UI 갱신은 기존 프로젝트 컨벤션(폴링)을 따르도록 CurrentMp/MaxMp를 그대로 노출하되, 필요한 곳(스킬 사용 가능 여부 표시 등)을 위해
/// 변화 이벤트도 함께 제공한다.
/// </summary>
public class ManaComponent : MonoBehaviour
{
    /// <summary>(현재 마나, 최대 마나). 서버 값이든 초기화든 값이 바뀔 때마다 발화된다.</summary>
    public event Action<int, int> OnManaChanged;

    private int maxMp;
    private int currentMp;

    public int MaxMp => maxMp;
    public int CurrentMp => currentMp;

    /// <summary>
    /// 외부 값으로 마나를 초기화한다(스폰 시 최초 1회). currentMp가 maxMp를 넘거나 음수가 되지 않도록 보정한다.
    /// 이후 마나 변화는 서버 결과를 ApplyServerMp로 반영한다.
    /// </summary>
    public void ApplyMana(int newMaxMp, int newCurrentMp)
    {
        maxMp = Mathf.Max(0, newMaxMp);
        currentMp = Mathf.Clamp(newCurrentMp, 0, maxMp);

        OnManaChanged?.Invoke(currentMp, maxMp);
    }

    /// <summary>
    /// UserStats(intel)와 레벨로부터 최대 마나를 계산해 가득 찬 상태로 초기화한다(스폰 시 최초 1회). 서버(CombatStatCalculator.CalculateMaxMp)와
    /// 같은 공식(ManaFormula)이고, 이후 값은 서버가 보낸 것을 그대로 쓰므로 이 값은 스폰 직후 서버 응답(Game_EnterAck)을 받기 전까지의 표시값이다.
    /// </summary>
    public void ApplyFromUserStats(UserStats userStats, int level)
    {
        if (userStats == null)
        {
            return;
        }

        int calculatedMaxMp = ManaFormula.CalculateMaxMp(userStats.intel, level);
        ApplyMana(calculatedMaxMp, calculatedMaxMp);
    }

    /// <summary>GameServer가 계산한 마나(입장/레벨업/부활/회복/소모 후 값)를 그대로 반영한다.</summary>
    public void ApplyServerMp(int newCurrentMp, int newMaxMp)
    {
        ApplyMana(newMaxMp, newCurrentMp);
    }
}
