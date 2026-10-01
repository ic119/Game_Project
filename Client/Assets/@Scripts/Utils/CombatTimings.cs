namespace Incheol.Utils
{
    // 서버(GameServer CombatTuning)의 전투 설정과 짝을 이루는 클라이언트 쪽 시간 값. 서버가 권위로 판정하는 값(대쉬 무적, 최소 대쉬 간격,
    // 부활 대기, 최소 공격 간격)은 여기 값과 아래 관계를 지켜야 하고, GameServer.Tests의 CombatTuningTests가 이를 검사한다.
    //   - 서버 대쉬 무적 시간 >= DashDurationSeconds (대쉬 동작 내내 무적이어야 한다)
    //   - 서버 최소 대쉬 간격 <= DashDurationSeconds + DashCooldownSeconds (정상 연속 대쉬가 거부되면 안 된다)
    //   - 서버 부활 대기 == ReviveDelaySeconds (부활 팝업 카운트다운과 같아야 한다)
    //   - 서버 최소 공격 간격 < ComboInputGuardSeconds (정상 콤보 2타 요청이 드롭되면 안 된다)
    // 값을 바꾸면 서버 설정(appsettings.json의 Combat 섹션)과 함께 확인한다. Unity 의존이 없어 서버 테스트 프로젝트가 그대로 링크한다.
    public static class CombatTimings
    {
        public const float DashDurationSeconds = 0.25f;
        public const float DashCooldownSeconds = 1f;
        public const float ReviveDelaySeconds = 5f;
        public const float ComboInputGuardSeconds = 0.15f;

        // 목록에 없는 무기 타입이 쓰는 기본 공격 길이(16프레임, 30fps).
        public const float DefaultAttackDurationSeconds = 16f / 30f;

        // Attack Layer의 Attack1/Attack2 BlendTree는 WeaponIndex 기준이라 무기마다 재생되는 클립(길이)이 다르다
        // (예: Spear는 Attack01 16프레임/Attack02 20프레임). weaponIndex는 WeaponType의 정수 값이다
        // (None 0, OneHanded 1, TwoHanded 2, Wand 4, Spear 5). 로컬 플레이어(PlayerAttackController)와
        // 원격 플레이어(RemoteCharacterController)가 같은 표를 쓰도록 여기 한 곳에만 둔다.
        private static readonly (int WeaponIndex, float Attack1Seconds, float Attack2Seconds)[] WeaponAttackTable =
        {
            (1, 16f / 30f, 16f / 30f),
            (2, 18f / 30f, 18f / 30f),
            (4, 16f / 30f, 16f / 30f),
            (5, 16f / 30f, 20f / 30f),
        };

        // 등록된 무기면 true. 등록되지 않았으면 false와 함께 기본 길이를 돌려준다(호출측이 경고를 남길 수 있게 구분한다).
        public static bool TryGetAttackDuration(int weaponIndex, int comboStage, int maxComboStage, out float seconds)
        {
            foreach (var timing in WeaponAttackTable)
            {
                if (timing.WeaponIndex == weaponIndex)
                {
                    seconds = comboStage >= maxComboStage ? timing.Attack2Seconds : timing.Attack1Seconds;
                    return true;
                }
            }

            seconds = DefaultAttackDurationSeconds;
            return false;
        }
    }
}
