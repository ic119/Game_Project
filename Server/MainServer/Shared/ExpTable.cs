namespace Shared
{
    // 레벨업 경험치 곡선. 클라이언트(Assets/@Scripts/Utils/ExpTable.cs)는 경험치 바 UI 표시용으로
    // 이 공식을 그대로 복제해 갖고 있다 - 레벨업 판정 자체(권위)는 이 서버 쪽만 수행한다.
    // 값을 튜닝하려면 이 파일과 클라이언트 쪽 복제본 둘 다 같은 값으로 맞춰야 한다.
    public static class ExpTable
    {
        public const int MaxLevel = 30;

        // level에서 level+1로 올라가기 위해 필요한 경험치. level >= MaxLevel이면 더 이상 필요치가 없다(0).
        public static int GetRequiredExp(int level)
        {
            if (level < 1 || level >= MaxLevel)
            {
                return 0;
            }

            return (int)System.Math.Round(100.0 * System.Math.Pow(level, 1.5));
        }

        // 이미 만렙이면 아무것도 하지 않고 false를 반환한다(경험치 자체를 지급하지 않음).
        // 그렇지 않으면 exp를 더하고, 필요치를 넘는 동안 반복해서 레벨을 올린 뒤(다중 레벨업 지원) true를 반환한다.
        public static bool TryApplyExp(ref int level, ref int exp, int gainedExp, out int expToNextLevel)
        {
            if (level >= MaxLevel)
            {
                expToNextLevel = 0;
                return false;
            }

            exp += gainedExp;

            int required = GetRequiredExp(level);
            while (level < MaxLevel && required > 0 && exp >= required)
            {
                exp -= required;
                level++;
                required = GetRequiredExp(level);
            }

            expToNextLevel = required;
            return true;
        }
    }
}
