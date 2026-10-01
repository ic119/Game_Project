using GameServer.Combat;

namespace GameServer.Tests;

// 몬스터 공격 선딜 판정 규칙(대쉬 회피)을 고정해두는 회귀 테스트. 판정 규칙을 바꾸면 클라이언트 연출(회피 이펙트가
// 나오는 조건)도 함께 확인해야 한다.
public class MonsterAttackJudgeTests
{
    [Fact]
    public void InRangeAndNotInvulnerable_Hits()
    {
        Assert.Equal(MonsterAttackOutcome.Hit, MonsterAttackJudge.Judge(isInRange: true, isInvulnerable: false, dashedDuringWindup: false));
    }

    [Fact]
    public void InRangeButInvulnerable_IsDodged()
    {
        // 몬스터 쪽으로 파고드는 전방 대쉬처럼 사거리 안에 남아 있어도 무적이면 회피다.
        Assert.Equal(MonsterAttackOutcome.Dodged, MonsterAttackJudge.Judge(isInRange: true, isInvulnerable: true, dashedDuringWindup: true));
    }

    [Fact]
    public void OutOfRangeAfterDashingDuringWindup_IsDodged()
    {
        // 후방 대쉬가 끝나 무적은 풀렸지만 선딜 동안 대쉬해서 사거리를 벗어났다.
        Assert.Equal(MonsterAttackOutcome.Dodged, MonsterAttackJudge.Judge(isInRange: false, isInvulnerable: false, dashedDuringWindup: true));
    }

    [Fact]
    public void OutOfRangeWithoutDashing_IsWhiffAndNotReportedAsDodge()
    {
        // 그냥 걸어서 벗어난 경우는 빗나갈 뿐 회피 연출을 주지 않는다.
        Assert.Equal(MonsterAttackOutcome.Whiff, MonsterAttackJudge.Judge(isInRange: false, isInvulnerable: false, dashedDuringWindup: false));
    }

    [Fact]
    public void InvulnerableWinsEvenWhenNotDashedDuringThisWindup()
    {
        // 이전 선딜 중에 시작한 대쉬의 무적이 아직 남아 있는 경우도 피해는 막는다.
        Assert.Equal(MonsterAttackOutcome.Dodged, MonsterAttackJudge.Judge(isInRange: true, isInvulnerable: true, dashedDuringWindup: false));
    }
}
