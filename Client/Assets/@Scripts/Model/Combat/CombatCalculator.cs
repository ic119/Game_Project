using UnityEngine;

/// <summary>
/// 전투 수치 계산을 모아두는 순수함수 유틸. 상태를 갖지 않으며, 실제 밸런스 기획이 정해지면
/// 이 클래스의 메서드만 바꾸면 된다(CombatStatComponent.ApplyFromUserStats와 같은 성격의 임시 공식).
/// </summary>
public static class CombatCalculator
{
    /// <summary>
    /// 방어력을 차감한 최종 데미지를 계산한다. 방어력이 아무리 높아도 최소 1은 들어가도록 해
    /// 무한 방어로 인한 무적 상태를 막는다.
    /// </summary>
    public static int ApplyDefense(int rawDamage, int defense)
    {
        return Mathf.Max(1, rawDamage - defense);
    }
}
