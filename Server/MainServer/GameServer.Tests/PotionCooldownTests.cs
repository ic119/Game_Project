using GameServer.Items;

namespace GameServer.Tests;

public class PotionCooldownTests
{
    // 시간을 직접 움직이는 가짜 시계(밀리초).
    private sealed class FakeClock
    {
        public long Now { get; set; } = 1_000_000;
        public long Read() => Now;
    }

    private static (PotionCooldown Cooldown, FakeClock Clock) Create()
    {
        var clock = new FakeClock();
        return (new PotionCooldown(clock.Read), clock);
    }

    [Fact]
    public void FirstUse_IsAllowed()
    {
        var (cooldown, _) = Create();

        Assert.True(cooldown.TryReserve(2f, out _, out long remaining));
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void SecondUseDuringCooldown_IsBlockedWithRemainingTime()
    {
        var (cooldown, clock) = Create();
        cooldown.TryReserve(6f, out _, out _);

        clock.Now += 2500;

        Assert.False(cooldown.TryReserve(6f, out _, out long remaining));
        Assert.Equal(3500, remaining);
    }

    [Fact]
    public void UseAfterCooldownElapsed_IsAllowed()
    {
        var (cooldown, clock) = Create();
        cooldown.TryReserve(6f, out _, out _);

        clock.Now += 6000;

        Assert.True(cooldown.TryReserve(6f, out _, out _));
    }

    [Fact]
    public void CooldownIsSharedAcrossPotionTypes_LargeBlocksSmall()
    {
        // 대형(12초)을 쓴 직후에는 소형(2초)도 못 쓴다 - 종류별로 따로였다면 연달아 100% 회복이 가능했다.
        var (cooldown, clock) = Create();
        cooldown.TryReserve(12f, out _, out _);

        clock.Now += 1000;

        Assert.False(cooldown.TryReserve(2f, out _, out long remaining));
        Assert.Equal(11000, remaining);
    }

    [Fact]
    public void CooldownDurationFollowsTheLastUsedPotion()
    {
        var (cooldown, clock) = Create();
        cooldown.TryReserve(2f, out _, out _);      // 소형
        clock.Now += 2000;
        Assert.True(cooldown.TryReserve(12f, out _, out _)); // 소형 대기가 끝났으니 대형 사용 가능

        clock.Now += 2000;                           // 대형 12초 중 2초 경과

        Assert.False(cooldown.TryReserve(2f, out _, out long remaining));
        Assert.Equal(10000, remaining);
    }

    [Fact]
    public void Cancel_RestoresPreviousState_SoFailedAttemptDoesNotConsumeCooldown()
    {
        var (cooldown, _) = Create();
        cooldown.TryReserve(12f, out PotionCooldown.Reservation reservation, out _);

        cooldown.Cancel(reservation);

        Assert.Equal(0, cooldown.RemainingMs());
        Assert.True(cooldown.TryReserve(12f, out _, out _));
    }

    [Fact]
    public void Cancel_DoesNotEraseAnEarlierStillActiveCooldown()
    {
        // 이전 사용(대기 중이 아닌 상태에서 시작)의 예약을 취소해도, 그 이전 상태(대기 없음)로만 돌아간다.
        var (cooldown, clock) = Create();
        cooldown.TryReserve(6f, out _, out _);
        clock.Now += 6000;
        cooldown.TryReserve(2f, out PotionCooldown.Reservation second, out _);

        cooldown.Cancel(second);

        Assert.Equal(0, cooldown.RemainingMs());
    }

    [Fact]
    public void ZeroCooldownPotion_DoesNotBlockNextUse_ButRespectsActiveCooldown()
    {
        var (cooldown, clock) = Create();
        Assert.True(cooldown.TryReserve(0f, out _, out _));
        Assert.True(cooldown.TryReserve(0f, out _, out _)); // 대기시간 없는 물약은 연속 사용 가능

        cooldown.TryReserve(6f, out _, out _);
        clock.Now += 1000;

        Assert.False(cooldown.TryReserve(0f, out _, out _)); // 다른 물약의 대기 중에는 막힌다
    }

    [Fact]
    public void RemainingMs_CountsDownAndStopsAtZero()
    {
        var (cooldown, clock) = Create();
        cooldown.TryReserve(2f, out _, out _);

        Assert.Equal(2000, cooldown.RemainingMs());
        clock.Now += 500;
        Assert.Equal(1500, cooldown.RemainingMs());
        clock.Now += 5000;
        Assert.Equal(0, cooldown.RemainingMs());
    }

    [Fact]
    public async Task ConcurrentReservations_OnlyOneSucceeds()
    {
        // 소모(MainServer 왕복) 중에 같은 세션에서 요청이 겹쳐도 하나만 통과해야 한다.
        var (cooldown, _) = Create();
        int successes = 0;

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            if (cooldown.TryReserve(12f, out _, out _))
            {
                Interlocked.Increment(ref successes);
            }
        })));

        Assert.Equal(1, successes);
    }

    [Fact]
    public void DefaultClock_WorksWithoutInjection()
    {
        var cooldown = new PotionCooldown();

        Assert.True(cooldown.TryReserve(30f, out _, out _));
        Assert.False(cooldown.TryReserve(30f, out _, out long remaining));
        Assert.InRange(remaining, 1, 30_000);
    }
}
