/// <summary>
/// 데미지를 받을 수 있는 모든 대상(유저 캐릭터, 향후 몬스터 등)의 공통 계약.
/// 공격 주체는 구체 타입을 몰라도 IDamageable만으로 피격 판정을 넘길 수 있다.
/// </summary>
public interface IDamageable
{
    bool IsDead { get; }

    void TakeDamage(DamageInfo damageInfo);
}
