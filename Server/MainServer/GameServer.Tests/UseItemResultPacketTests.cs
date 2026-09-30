using Shared.Networking.Packets;

namespace GameServer.Tests;

public class UseItemResultPacketTests
{
    [Fact]
    public void Success_RoundTripsWithCooldownStarted()
    {
        var packet = new S2CUseItemResult
        {
            ItemId = "potion_hp_large",
            Success = true,
            FailReason = UseItemFailReason.None,
            CooldownRemainingMs = 12000
        };

        S2CUseItemResult decoded = S2CUseItemResult.Decode(packet.Encode());

        Assert.Equal("potion_hp_large", decoded.ItemId);
        Assert.True(decoded.Success);
        Assert.Equal(UseItemFailReason.None, decoded.FailReason);
        Assert.Equal(12000, decoded.CooldownRemainingMs);
    }

    [Theory]
    [InlineData(UseItemFailReason.Cooldown, 3500)]
    [InlineData(UseItemFailReason.NotUsable, 0)]
    [InlineData(UseItemFailReason.Rejected, 0)]
    [InlineData(UseItemFailReason.TooFast, 1200)]
    public void Failure_RoundTripsReasonAndRemainingCooldown(UseItemFailReason reason, int remainingMs)
    {
        var packet = new S2CUseItemResult { ItemId = "potion_hp_small", Success = false, FailReason = reason, CooldownRemainingMs = remainingMs };

        S2CUseItemResult decoded = S2CUseItemResult.Decode(packet.Encode());

        Assert.False(decoded.Success);
        Assert.Equal(reason, decoded.FailReason);
        Assert.Equal(remainingMs, decoded.CooldownRemainingMs);
    }

    [Fact]
    public void FailReasonNumbers_AreStable()
    {
        // 클라이언트 GameUseItemFailReason과 같은 번호를 쓰므로 바꾸면 안 된다.
        Assert.Equal(0, (byte)UseItemFailReason.None);
        Assert.Equal(1, (byte)UseItemFailReason.Cooldown);
        Assert.Equal(2, (byte)UseItemFailReason.NotUsable);
        Assert.Equal(3, (byte)UseItemFailReason.Rejected);
        Assert.Equal(4, (byte)UseItemFailReason.TooFast);
    }
}
