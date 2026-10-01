namespace Shared
{
    // 레벨업에 따른 능력치(str/agi/intel) 자동 성장 규칙. 모든 능력치가 같은 양만큼 오르고, 값은 레벨에서 바로 계산한다 -
    // DB에 따로 저장하지 않으므로(Character.Str/Agi/Intel은 생성 시 기본값 그대로) 스키마 변경도, 저장 순서 문제도 없다.
    // 서버 권위 계산은 GameServer의 CombatStatCalculator가 이 함수로 하고, 클라이언트(Assets/@Scripts/Utils/StatGrowth.cs)는
    // 화면 표시용으로 같은 공식을 복제해 갖고 있다(ExpTable과 같은 구조). 값을 튜닝하려면 두 파일을 같은 값으로 맞춰야 한다.
    //
    // 증가량은 2레벨마다 +1(레벨당 +0.5)이다. 몬스터 밸런싱이 "장비 세트가 전투력의 주축"이라는 전제로 짜여 있어서,
    // 레벨당 +1은 후반에 성장분이 장비 효과와 비슷해져 단계 구분이 무너진다(Server/몬스터_밸런싱_공식.txt 참고).
    public static class StatGrowth
    {
        // 이 레벨 수마다 모든 능력치가 1 오른다.
        public const int LevelsPerStatPoint = 2;

        // level에서 받는 누적 능력치 보너스. 1레벨은 0이고 3, 5, 7... 레벨에 1씩 늘어난다(예: 10레벨 +4, 28레벨 +13, 30레벨 +14).
        // 1 미만 레벨(잘못된 값)은 보너스 없음으로 취급한다.
        public static int BonusAtLevel(int level)
        {
            return level <= 1 ? 0 : (level - 1) / LevelsPerStatPoint;
        }
    }
}
