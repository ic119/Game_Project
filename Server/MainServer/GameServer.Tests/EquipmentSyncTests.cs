using GameServer.Combat;
using Shared.Networking.Packets;

namespace GameServer.Tests;

public class EquipmentSyncTests
{
    private static CharacterSnapshot Snapshot(Dictionary<string, string>? bySlot) =>
        new("tester", 0, 0, 0, 1, 0, 10, 10, bySlot?.Values.ToList() ?? new List<string>(), bySlot);

    [Fact]
    public void EquippedVisuals_PicksItemIdPerSlot()
    {
        var snapshot = Snapshot(new Dictionary<string, string>
        {
            ["Weapon"] = "weapon_a",
            ["Armor"] = "armor_a",
            ["Helmet"] = "helmet_a"
        });

        EquippedVisuals visuals = EquippedVisuals.From(snapshot, _ => true);

        Assert.Equal("weapon_a", visuals.WeaponItemId);
        Assert.Equal("armor_a", visuals.ArmorItemId);
        Assert.Equal("helmet_a", visuals.HelmetItemId);
    }

    [Fact]
    public void EquippedVisuals_EmptySlotsAreEmptyStrings()
    {
        EquippedVisuals visuals = EquippedVisuals.From(Snapshot(new Dictionary<string, string> { ["Weapon"] = "weapon_a" }), _ => true);

        Assert.Equal("weapon_a", visuals.WeaponItemId);
        Assert.Equal(string.Empty, visuals.ArmorItemId);
        Assert.Equal(string.Empty, visuals.HelmetItemId);
    }

    [Fact]
    public void EquippedVisuals_UnknownItemIsTreatedAsEmpty()
    {
        // 삭제된 아이템(예: 제거된 방패)이 DB에 장착 상태로 남아 있어도 외형에서는 빈 슬롯으로 취급한다.
        var snapshot = Snapshot(new Dictionary<string, string> { ["Weapon"] = "weapon_shield01", ["Armor"] = "armor_a" });

        EquippedVisuals visuals = EquippedVisuals.From(snapshot, id => id != "weapon_shield01");

        Assert.Equal(string.Empty, visuals.WeaponItemId);
        Assert.Equal("armor_a", visuals.ArmorItemId);
    }

    [Fact]
    public void EquippedVisuals_SnapshotWithoutSlotInfo_IsAllEmpty()
    {
        EquippedVisuals visuals = EquippedVisuals.From(Snapshot(null), _ => true);

        Assert.Equal(new EquippedVisuals(string.Empty, string.Empty, string.Empty), visuals);
    }

    [Fact]
    public void CharacterSnapshot_ParsesEquippedItemsBySlot()
    {
        const string json = """
        {
          "_nickname": "n", "_hairIndex": 1, "_eyeIndex": 2, "_mouthIndex": 3, "_level": 4, "_exp": 5, "_str": 6, "_agi": 7,
          "_items": [
            { "_itemId": "weapon_a", "_equipSlot": "Weapon" },
            { "_itemId": "armor_a", "_equipSlot": "Armor" },
            { "_itemId": "potion_hp_small", "_equipSlot": null }
          ]
        }
        """;

        CharacterSnapshot? snapshot = CharacterSnapshot.FromCharacterResponseJson(json);

        Assert.NotNull(snapshot);
        Assert.Equal("weapon_a", snapshot!.EquippedBySlot!["Weapon"]);
        Assert.Equal("armor_a", snapshot.EquippedBySlot["Armor"]);
        Assert.False(snapshot.EquippedBySlot.ContainsKey("Helmet"));
        Assert.Equal(2, snapshot.EquippedItemIds.Count);
    }

    [Fact]
    public void PlayerInfo_RoundTripsEquipmentFields()
    {
        var info = new PlayerInfo
        {
            PlayerId = 7,
            Nickname = "tester",
            WeaponItemId = "weapon_ohs05_sword",
            ArmorItemId = "armor_body11",
            HelmetItemId = string.Empty
        };

        PlayerInfo decoded = PlayerInfo.Decode(info.Encode());

        Assert.Equal("weapon_ohs05_sword", decoded.WeaponItemId);
        Assert.Equal("armor_body11", decoded.ArmorItemId);
        Assert.Equal(string.Empty, decoded.HelmetItemId);
        Assert.Equal("tester", decoded.Nickname);
    }

    [Fact]
    public void EquipmentChangedBroadcast_RoundTrips()
    {
        var packet = new S2CEquipmentChangedBroadcast
        {
            PlayerId = 9,
            WeaponItemId = "w",
            ArmorItemId = string.Empty,
            HelmetItemId = "h"
        };

        S2CEquipmentChangedBroadcast decoded = S2CEquipmentChangedBroadcast.Decode(packet.Encode());

        Assert.Equal(9, decoded.PlayerId);
        Assert.Equal("w", decoded.WeaponItemId);
        Assert.Equal(string.Empty, decoded.ArmorItemId);
        Assert.Equal("h", decoded.HelmetItemId);
    }
}
