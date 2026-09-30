using Shared.Networking.Packets;

namespace GameServer.Tests;

public class ActiveChestsPacketTests
{
    [Fact]
    public void EncodeDecode_RoundTripsAllFields()
    {
        var packet = new S2CActiveChests
        {
            Chests =
            {
                new ActiveChestInfo { Id = "chest_a", X = 1.5f, Y = 2f, Z = -3.25f, LootTableKey = "TreasureChestBasic" },
                new ActiveChestInfo { Id = "한글_후보", X = 10f, Y = 0f, Z = 7f, LootTableKey = "TreasureChestHidden" }
            }
        };

        S2CActiveChests decoded = S2CActiveChests.Decode(packet.Encode());

        Assert.Equal(2, decoded.Chests.Count);
        Assert.Equal("chest_a", decoded.Chests[0].Id);
        Assert.Equal(-3.25f, decoded.Chests[0].Z);
        Assert.Equal("한글_후보", decoded.Chests[1].Id);
        Assert.Equal("TreasureChestHidden", decoded.Chests[1].LootTableKey);
    }

    [Fact]
    public void EncodeDecode_EmptyList_RoundTrips()
    {
        S2CActiveChests decoded = S2CActiveChests.Decode(new S2CActiveChests().Encode());

        Assert.Empty(decoded.Chests);
    }
}
