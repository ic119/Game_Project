using System;

namespace Incheol.Utils
{
    // 서버(GameServer CombatStatCalculator.CalculateMaxMp)와 공식이 반드시 일치해야 한다. 최대 마나는 체력처럼 DB에 저장하지 않고
    // 지능과 레벨에서 바로 계산한다 - 서버가 권위로 계산하고(입장/레벨업 때 값을 내려준다), 클라이언트는 스폰 직후 서버 응답을 받기 전까지의
    // 표시값으로 같은 공식을 쓴다(ExpTable/StatGrowth와 같은 구조). 값을 튜닝하려면 서버 CombatStatCalculator와 이 파일을 같은 값으로
    // 맞추고, 근거와 레벨별 표는 Server/마나_밸런싱_공식.txt를 갱신한다. Unity 의존이 없어 서버 테스트 프로젝트가 그대로 링크해
    // ClientFormulaParityTests가 두 공식이 같은 값을 내는지 검사한다.
    public static class ManaFormula
    {
        public const int BaseMaxMp = 30;
        public const int MaxMpPerIntel = 3;
        public const int MaxMpPerLevel = 3;

        // 기본 지능(레벨 성장 보너스 제외)과 레벨로 최대 마나를 계산한다. 지능에는 레벨 성장 보너스(StatGrowth)가 더해진다.
        public static int CalculateMaxMp(int baseIntel, int level)
        {
            int intel = baseIntel + StatGrowth.BonusAtLevel(level);
            return BaseMaxMp + intel * MaxMpPerIntel + Math.Max(0, level - 1) * MaxMpPerLevel;
        }
    }
}
