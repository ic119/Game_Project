using GameServer.Monsters;

namespace GameServer.Tests;

// Monsters/MonsterDefinitions.json 픽스처에는 "TestMonster" 하나만 등록해뒀다.
public class MonsterDefinitionCatalogTests
{
    [Fact]
    public void Get_KnownType_ReturnsDefinitionFromFile()
    {
        MonsterDefinition definition = MonsterDefinitionCatalog.Get("TestMonster");

        Assert.Equal(100, definition.MaxHp);
        Assert.Equal(10, definition.AttackPower);
        Assert.Equal(2, definition.Defense);
        Assert.Equal(50, definition.ExpReward);
        Assert.Equal(5f, definition.DetectionRange);
        Assert.Equal(3f, definition.ChaseSpeed);
        Assert.Equal(9f, definition.LeashRange);
    }

    [Fact]
    public void Exists_KnownAndUnknownType()
    {
        Assert.True(MonsterDefinitionCatalog.Exists("TestMonster"));
        Assert.False(MonsterDefinitionCatalog.Exists("NoSuchMonster"));
    }

    [Fact]
    public void Get_UnknownType_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => MonsterDefinitionCatalog.Get("NoSuchMonster"));
    }

    [Fact]
    public void ParseAndValidate_OmittedFields_UseDefaults()
    {
        var result = MonsterDefinitionCatalog.ParseAndValidate("""{ "Slime": { "maxHp": 10 } }""", "test");

        Assert.Equal(10, result["Slime"].MaxHp);
        Assert.Equal(6f, result["Slime"].DetectionRange);
    }

    [Theory]
    [InlineData("""{ "X": { "maxHp": 0 } }""")]
    [InlineData("""{ "X": { "attackPower": -1 } }""")]
    [InlineData("""{ "X": { "defense": -1 } }""")]
    [InlineData("""{ "X": { "expReward": -1 } }""")]
    [InlineData("""{ "X": { "detectionRange": -1 } }""")]
    [InlineData("""{ "X": { "chaseSpeed": 0 } }""")]
    [InlineData("""{ "X": { "leashRange": 0 } }""")]
    public void ParseAndValidate_InvalidValue_Throws(string json)
    {
        Assert.Throws<InvalidOperationException>(() => MonsterDefinitionCatalog.ParseAndValidate(json, "test"));
    }

    [Fact]
    public void ParseAndValidate_MalformedJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MonsterDefinitionCatalog.ParseAndValidate("{ not json", "test"));
    }
}
