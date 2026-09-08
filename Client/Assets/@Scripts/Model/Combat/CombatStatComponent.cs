using UnityEngine;

// 공격력/방어력을 들고 있는 컴포넌트. 공격 주체(PlayerController 등)는 AttackPower를,
// HealthComponent는 Defense를 같은 GameObject에서 GetComponent로 참조해 공용으로 사용한다.
// PlayerCharacterModel뿐 아니라 향후 Enemy 쪽에도 그대로 붙여 재사용할 수 있도록
// 특정 캐릭터 종류에 의존하지 않는다.
public class CombatStatComponent : MonoBehaviour
{
    [Tooltip("ApplyFromUserStats가 호출되기 전까지 사용되는 기본 공격력/방어력.")]
    [SerializeField] private int baseAttackPower = 5;
    [SerializeField] private int baseDefense = 0;

    // 스킬/버프등으로 일시적으로 더해지는 공격력(예: PlayerController의 D스킬). baseAttackPower는 건드리지 않고
    // AttackPower를 읽을 때만 더해진다. 지속시간 관리는 호출측(PlayerController)의 몫이다.
    private int bonusAttackPower;

    public int AttackPower => baseAttackPower + bonusAttackPower;
    public int Defense => baseDefense;

    // UserStats(str/agi/intel)로부터 공격력/방어력을 계산해 반영한다.
    // str 1당 공격력 1, agi 2당 방어력 1로 잡은 임시 공식이며, 실제 밸런스 기획이 정해지면
    // 이 메서드 하나만 바꾸면 된다(PlaceholderMaxExp와 같은 성격의 임시값).
    public void ApplyFromUserStats(UserStats userStats)
    {
        if (userStats == null)
        {
            return;
        }

        baseAttackPower = userStats.str;
        baseDefense = userStats.agi / 2;
    }

    // 일시적인 공격력 보너스를 설정/해제한다. AttackPower에 그대로 더해지므로, 버프가 끝나면
    // 0을 넘겨 원래 수치로 되돌려야 한다(호출측이 타이머로 관리).
    public void SetBonusAttackPower(int amount)
    {
        bonusAttackPower = amount;
    }
}
