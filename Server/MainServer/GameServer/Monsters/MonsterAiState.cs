namespace GameServer.Monsters
{
    // 몬스터 한 마리의 추적 상태. GameRoom.MonsterRuntime이 들고 있으며 AI 틱마다 갱신된다.
    public enum MonsterAiState
    {
        // 스폰 위치에 정지, 매 틱 감지 범위 안의 플레이어를 탐색한다.
        Idle,

        // TargetPlayerId를 향해 이동한다. 대상이 사라지거나(퇴장/사망) 리쉬 범위를 벗어나면 Returning으로 전환한다.
        Chasing,

        // 스폰 위치로 복귀 중. 도착하면 Idle로 전환하고, 그 전까지는 재감지를 하지 않는다.
        Returning
    }
}
