namespace Incheol.Utils
{
    // 서버(Shared/StatGrowth.cs)와 공식이 반드시 일치해야 한다. 레벨업에 따른 능력치(str/agi/intel) 자동 성장 규칙으로,
    // 모든 능력치가 같은 양만큼 오르고 값은 레벨에서 바로 계산한다(DB에 따로 저장하지 않는다).
    // 서버가 공격력/방어력/최대 체력을 권위로 계산하고(GameServer CombatStatCalculator), 클라이언트는 화면 표시용으로
    // 같은 공식을 쓴다(ExpTable과 같은 구조). 값을 튜닝하려면 서버 파일과 이 파일을 같은 값으로 맞춰야 한다.
    public static class StatGrowth
    {
        // 이 레벨 수마다 모든 능력치가 1 오른다.
        public const int LevelsPerStatPoint = 2;

        // level에서 받는 누적 능력치 보너스. 1레벨은 0이고 3, 5, 7... 레벨에 1씩 늘어난다(예: 10레벨 +4, 28레벨 +13, 30레벨 +14).
        public static int BonusAtLevel(int level)
        {
            return level <= 1 ? 0 : (level - 1) / LevelsPerStatPoint;
        }
    }
}
