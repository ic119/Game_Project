using UnityEngine;

/// <summary>
/// 전투 수치 계산을 모아두는 순수함수 유틸. 상태를 갖지 않으며, 실제 밸런스 기획이 정해지면
/// 이 클래스의 메서드만 바꾸면 된다(CombatStatComponent.ApplyFromUserStats와 같은 성격의 임시 공식).
/// </summary>
public static class CombatCalculator
{
    // 피격 데미지(방어력 적용)는 GameServer가 계산한다(Server CombatStatCalculator.ApplyDefense) -
    // 클라이언트는 서버가 보낸 최종 피해량/남은 체력을 표시만 하므로 여기에 데미지 공식을 두지 않는다.

    /// <summary>
    /// 최대체력의 healPercent(%)만큼 회복량을 계산한다. 물약 등 회복 아이템 전용 공식이며,
    /// 비율이 아무리 낮아도(1% 등) 아무 효과가 없어 보이지 않도록 최소 1은 회복하도록 한다.
    /// </summary>
    public static int CalculateHealAmount(int maxHp, int healPercent)
    {
        return Mathf.Max(1, Mathf.RoundToInt(maxHp * healPercent / 100f));
    }
}
