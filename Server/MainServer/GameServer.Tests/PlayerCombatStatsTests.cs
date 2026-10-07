using GameServer.Combat;
using Shared;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 접속 중 레벨업/장비 변경이 공격력/방어력/최대 체력에 "즉시" 반영되는지 확인한다. GameRoom은 접속 세션 없이는 만들기 어려워서,
// 레벨업 때 GameRoom이 호출하는 PlayerCombatStats를 PlayerInfo 단위로 직접 검증한다(GameRoom.ApplyLevelUp은 이 호출 하나가 전부다).
public class PlayerCombatStatsTests
{
    // 입장 시점(레벨 level)의 플레이어를 서버가 만드는 순서를 그대로 재현한다: info.Level을 먼저 채우고 -> 스냅샷 적용 -> 최대 체력 계산.
    private static PlayerInfo Enter(int level, params string[] equippedItemIds)
    {
        var snapshot = new CharacterSnapshot("tester", 0, 0, 0, level, 0, 10, 10, equippedItemIds);
        var info = new PlayerInfo { PlayerId = 1, Level = level };
        PlayerCombatStats.ApplySnapshot(info, snapshot);
        info.MaxHp = CombatStatCalculator.CalculateMaxHp(snapshot);
        info.CurrentHp = info.MaxHp;
        info.MaxMp = CombatStatCalculator.CalculateMaxMp(snapshot);
        info.CurrentMp = info.MaxMp;
        return info;
    }

    [Fact]
    public void Enter_SetsBaseIntel_AndFullMana()
    {
        PlayerInfo info = Enter(1);

        Assert.Equal(CharacterSnapshot.DefaultIntel, info.BaseIntel);
        Assert.Equal(60, info.MaxMp);       // 30 + 10*3
        Assert.Equal(info.MaxMp, info.CurrentMp);
    }

    [Fact]
    public void ApplyLevelUp_GrowsMaxMana_AndRefillsMana()
    {
        PlayerInfo info = Enter(2); // 2레벨: 30 + 10*3 + 1*3 = 63
        Assert.Equal(63, info.MaxMp);
        info.CurrentMp = 4;
        info.ManaRegenRemainder = 0.7f;

        info.Level = 3;
        PlayerCombatStats.ApplyLevelUp(info);

        (int currentMp, int maxMp) = PlayerCombatStats.ReadMana(info);
        Assert.Equal(69, maxMp);            // 30 + (10+1)*3 + 2*3
        Assert.Equal(69, currentMp);        // 레벨업 때 가득 찬다
        Assert.Equal(0f, info.ManaRegenRemainder);
    }

    [Fact]
    public void Enter_AppliesLevelGrowthAndEquipment()
    {
        PlayerInfo info = Enter(1, "test_weapon_regression", "test_armor_regression");

        Assert.Equal(10 + 7, info.AttackPower);     // str 10 + 무기 7
        Assert.Equal(10 / 2 + 3, info.Defense);     // agi 10 / 2 + 방어구 3
        Assert.Equal(150, info.MaxHp);
    }

    [Fact]
    public void ApplyLevelUp_ReflectsNewLevelImmediately()
    {
        PlayerInfo info = Enter(2); // 2레벨: 아직 성장 보너스 없음 (공격 10, 방어 5, 최대 체력 160)
        Assert.Equal(10, info.AttackPower);
        Assert.Equal(160, info.MaxHp);

        info.Level = 3; // GameRoom.ApplyMonsterAttack이 경험치 처리로 먼저 레벨을 올린다
        (int currentHp, int maxHp) = PlayerCombatStats.ApplyLevelUp(info);

        Assert.Equal(11, info.AttackPower);  // 3레벨 보너스 +1
        Assert.Equal(5, info.Defense);       // (10+1)/2 = 5 (정수 나눗셈이라 이번 레벨은 방어력 그대로)
        Assert.Equal(175, maxHp);            // 100 + (10+1)*5 + (3-1)*10
        Assert.Equal(175, info.MaxHp);
        Assert.Equal(175, currentHp);
    }

    [Fact]
    public void ApplyLevelUp_RefillsHealth()
    {
        PlayerInfo info = Enter(4);
        info.CurrentHp = 5; // 거의 죽기 직전

        info.Level = 5;
        PlayerCombatStats.ApplyLevelUp(info);

        Assert.Equal(info.MaxHp, info.CurrentHp);
    }

    [Fact]
    public void ApplyLevelUp_HandlesMultipleLevelsAtOnce()
    {
        PlayerInfo info = Enter(1);

        info.Level = 10; // 큰 경험치로 한 번에 여러 레벨이 오르는 경우
        PlayerCombatStats.ApplyLevelUp(info);

        Assert.Equal(14, info.AttackPower);  // 보너스 +4
        Assert.Equal(7, info.Defense);       // (10+4)/2
        Assert.Equal(260, info.MaxHp);       // 100 + (10+4)*5 + 9*10
    }

    [Fact]
    public void ApplyLevelUp_KeepsEquipmentBonus()
    {
        PlayerInfo info = Enter(1, "test_weapon_regression", "test_armor_regression");

        info.Level = 10;
        PlayerCombatStats.ApplyLevelUp(info);

        Assert.Equal(10 + 4 + 7, info.AttackPower);
        Assert.Equal((10 + 4) / 2 + 3, info.Defense);
    }

    [Fact]
    public void ApplySnapshot_AfterLevelUp_UsesLiveLevelNotStaleDbLevel()
    {
        // 장비를 바꿔 재조회했을 때 DB의 레벨은 킬 보상 저장이 늦어 아직 1레벨일 수 있다 - 서버 메모리의 10레벨로 계산해야 한다.
        PlayerInfo info = Enter(1);
        info.Level = 10;
        PlayerCombatStats.ApplyLevelUp(info);

        var staleSnapshot = new CharacterSnapshot("tester", 0, 0, 0, 1, 0, 10, 10, new[] { "test_weapon_regression" });
        PlayerCombatStats.ApplySnapshot(info, staleSnapshot);

        Assert.Equal(10 + 4 + 7, info.AttackPower);  // 성장 보너스(+4)가 되돌아가지 않는다
        Assert.Equal((10 + 4) / 2, info.Defense);
    }

    [Fact]
    public void LevelUpThenEquipmentChange_DoesNotDoubleCountOrLoseGrowth()
    {
        // 레벨업과 장비 변경이 어느 순서로 와도 같은 결과여야 한다(증분이 아니라 절대값 재계산이라서).
        PlayerInfo levelFirst = Enter(1);
        levelFirst.Level = 10;
        PlayerCombatStats.ApplyLevelUp(levelFirst);
        PlayerCombatStats.ApplySnapshot(levelFirst, new CharacterSnapshot("tester", 0, 0, 0, 1, 0, 10, 10, new[] { "test_weapon_regression", "test_armor_regression" }));

        PlayerInfo equipFirst = Enter(1);
        PlayerCombatStats.ApplySnapshot(equipFirst, new CharacterSnapshot("tester", 0, 0, 0, 1, 0, 10, 10, new[] { "test_weapon_regression", "test_armor_regression" }));
        equipFirst.Level = 10;
        PlayerCombatStats.ApplyLevelUp(equipFirst);

        Assert.Equal(levelFirst.AttackPower, equipFirst.AttackPower);
        Assert.Equal(levelFirst.Defense, equipFirst.Defense);
        Assert.Equal(levelFirst.MaxHp, equipFirst.MaxHp);
        Assert.Equal(10 + 4 + 7, levelFirst.AttackPower);
    }

    [Fact]
    public void ApplyLevelUp_MatchesEnteringDirectlyAtThatLevel_ForEveryLevel()
    {
        // 1레벨로 입장해 레벨을 올려 가며 얻은 값이, 처음부터 그 레벨로 입장했을 때와 모든 레벨에서 같아야 한다.
        string[] gear = { "test_weapon_regression", "test_armor_regression" };
        PlayerInfo leveled = Enter(1, gear);

        for (int level = 2; level <= ExpTable.MaxLevel; level++)
        {
            leveled.Level = level;
            PlayerCombatStats.ApplyLevelUp(leveled);

            PlayerInfo direct = Enter(level, gear);

            Assert.Equal(direct.AttackPower, leveled.AttackPower);
            Assert.Equal(direct.Defense, leveled.Defense);
            Assert.Equal(direct.MaxHp, leveled.MaxHp);
        }
    }

    [Fact]
    public void BaseValuesAreServerOnly_NotSentOverTheNetwork()
    {
        PlayerInfo info = Enter(10, "test_weapon_regression");

        PlayerInfo roundTripped = PlayerInfo.Decode(info.Encode());

        Assert.Equal(info.AttackPower, roundTripped.AttackPower); // 계산된 결과는 전달된다
        Assert.Equal(0, roundTripped.BaseStr);                    // 서버 전용 기준값은 전달되지 않는다
        Assert.Equal(0, roundTripped.EquipmentAttackBonus);
    }
}
