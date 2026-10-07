namespace GameServer.Tests;

// 클라이언트가 화면 표시용으로 따로 들고 있는 공식(Utils/ExpTable.cs, Utils/StatGrowth.cs, 테스트 프로젝트에 링크됨)이
// 서버 권위 공식(Shared)과 모든 레벨에서 같은 값을 내는지 확인한다. 한쪽만 튜닝하면 이 테스트가 실패한다.
public class ClientFormulaParityTests
{
    [Fact]
    public void ExpTable_ClientMatchesServer_ForAllLevels()
    {
        Assert.Equal(Shared.ExpTable.MaxLevel, Incheol.Utils.ExpTable.MaxLevel);

        var mismatches = new List<string>();
        for (int level = -1; level <= Shared.ExpTable.MaxLevel + 10; level++)
        {
            int server = Shared.ExpTable.GetRequiredExp(level);
            int client = Incheol.Utils.ExpTable.GetRequiredExp(level);
            if (server != client)
            {
                mismatches.Add($"Lv{level}: 서버 {server} / 클라 {client}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void StatGrowth_ClientMatchesServer_ForAllLevels()
    {
        Assert.Equal(Shared.StatGrowth.LevelsPerStatPoint, Incheol.Utils.StatGrowth.LevelsPerStatPoint);

        var mismatches = new List<string>();
        for (int level = -1; level <= 200; level++)
        {
            int server = Shared.StatGrowth.BonusAtLevel(level);
            int client = Incheol.Utils.StatGrowth.BonusAtLevel(level);
            if (server != client)
            {
                mismatches.Add($"Lv{level}: 서버 {server} / 클라 {client}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void MaxMp_ClientMatchesServer_ForAllIntelAndLevels()
    {
        Assert.Equal(GameServer.Combat.CombatStatCalculator.BaseMaxMp, Incheol.Utils.ManaFormula.BaseMaxMp);
        Assert.Equal(GameServer.Combat.CombatStatCalculator.MaxMpPerIntel, Incheol.Utils.ManaFormula.MaxMpPerIntel);
        Assert.Equal(GameServer.Combat.CombatStatCalculator.MaxMpPerLevel, Incheol.Utils.ManaFormula.MaxMpPerLevel);

        var mismatches = new List<string>();
        for (int intel = 0; intel <= 60; intel++)
        {
            for (int level = -1; level <= 100; level++)
            {
                int server = GameServer.Combat.CombatStatCalculator.CalculateMaxMp(intel, level);
                int client = Incheol.Utils.ManaFormula.CalculateMaxMp(intel, level);
                if (server != client)
                {
                    mismatches.Add($"지능{intel}/Lv{level}: 서버 {server} / 클라 {client}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches.Take(10)));
    }
}
