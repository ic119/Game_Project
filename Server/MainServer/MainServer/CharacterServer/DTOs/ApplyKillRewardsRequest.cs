namespace MainServer.CharacterServer.DTOs
{
    // GameServer(TCP)가 몬스터 처치 시 계산한 보상을 클라이언트가 한 번에 저장 요청할 때 쓴다.
    // _level/_exp는 UpdateCharacterProgressRequest와 동일하게 서버가 계산한 최종 값을 그대로 싣고,
    // _goldGained/_items는 델타(더할 값)다 - 골드/아이템은 다른 경로(퀘스트 등)로도 늘어날 수 있어
    // 최종값이 아니라 증가분으로 주고받는 편이 여러 지급 경로를 나중에 추가하기 쉽다.
    public record ApplyKillRewardsRequest(int _level, int _exp, long _goldGained, List<KillRewardItem> _items);

    public record KillRewardItem(string _itemId, int _qty);
}
