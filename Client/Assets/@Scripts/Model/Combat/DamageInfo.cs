/// <summary>
/// 피격 한 건을 표현하는 값(방어력 적용 전 원본 데미지). 매 타격마다 생성될 수 있어
/// GC 압박을 피하려고 class가 아닌 struct로 둔다.
/// </summary>
public readonly struct DamageInfo
{
    public readonly long AttackerId;
    public readonly int Amount;

    public DamageInfo(long attackerId, int amount)
    {
        AttackerId = attackerId;
        Amount = amount;
    }
}
