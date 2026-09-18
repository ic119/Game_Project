using UnityEngine;

namespace Incheol.Utils
{
    // 서버(Shared/ExpTable.cs)와 공식이 반드시 일치해야 한다. 레벨업 판정(권위) 자체는 서버만 수행하고,
    // 여기서는 스폰 직후 아직 서버로부터 Game_ExpGainBroadcast를 한 번도 못 받은 시점에 경험치 바의
    // "다음 레벨까지 필요한 경험치"를 화면에 표시하기 위해서만 쓴다.
    public static class ExpTable
    {
        public const int MaxLevel = 30;

        public static int GetRequiredExp(int level)
        {
            if (level < 1 || level >= MaxLevel)
            {
                return 0;
            }

            return Mathf.RoundToInt(100f * Mathf.Pow(level, 1.5f));
        }
    }
}
