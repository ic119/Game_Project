namespace GameServer.Combat
{
    // 몬스터 공격 선딜이 끝난 순간(GameRoom.ResolveMonsterAttack)의 판정 규칙. 서버 권위로 한 곳에서만 정하고,
    // 대쉬 회피의 의도(거리 이탈 + 무적)가 코드 곳곳에 흩어지지 않도록 분리했다.
    public enum MonsterAttackOutcome
    {
        // 사거리 안이고 무적도 아니다 - 피해를 준다.
        Hit,

        // 대쉬 무적에 걸렸거나, 선딜 도중 대쉬해서 사거리를 벗어났다 - 피해 없음, 회피 연출을 알린다.
        Dodged,

        // 대쉬와 무관하게 그냥 사거리를 벗어났다 - 피해 없음, 따로 알리지 않는다(이미 재생된 공격 모션만 헛스윙으로 보인다).
        Whiff
    }

    public static class MonsterAttackJudge
    {
        // 공격 시작은 MeleeAttackRange 안에서만 가능하지만, 판정은 선딜이 끝난 뒤 갱신된 위치(클라이언트가 0.1초마다 보고)로 한다.
        // 가만히 서 있던 플레이어가 위치 보고 지터만으로 경계 밖으로 판정돼 빗나가지 않도록 약간의 여유를 준다.
        public const float HitRangeTolerance = 0.25f;

        // isInvulnerable: 판정 시각이 대쉬 무적 구간 안인가.
        // dashedDuringWindup: 이번 공격의 선딜이 시작된 뒤에 대쉬를 시작했는가.
        public static MonsterAttackOutcome Judge(bool isInRange, bool isInvulnerable, bool dashedDuringWindup)
        {
            if (isInvulnerable)
            {
                return MonsterAttackOutcome.Dodged;
            }

            if (isInRange)
            {
                return MonsterAttackOutcome.Hit;
            }

            return dashedDuringWindup ? MonsterAttackOutcome.Dodged : MonsterAttackOutcome.Whiff;
        }
    }
}
