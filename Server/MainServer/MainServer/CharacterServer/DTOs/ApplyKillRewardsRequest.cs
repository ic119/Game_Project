namespace MainServer.CharacterServer.DTOs
{
    // GameServer(TCP)가 몬스터 처치 시 계산한 보상을 서버 간 API(api/internal/...)로 한 번에 저장할 때 쓴다.
    // _level/_exp는 GameServer가 계산한 최종 값을 그대로 싣고,
    // _goldGained/_items는 델타(더할 값)다 - 골드/아이템은 다른 경로(퀘스트 등)로도 늘어날 수 있어
    // 최종값이 아니라 증가분으로 주고받는 편이 여러 지급 경로를 나중에 추가하기 쉽다.
    // _rewardId는 GameServer가 보상마다 발급하는 id로, 재시도로 같은 보상이 다시 와도 한 번만 반영하는 데 쓴다(KillRewardReceipt).
    public record ApplyKillRewardsRequest(Guid _rewardId, int _level, int _exp, long _goldGained, List<KillRewardItem> _items);

    public record KillRewardItem(string _itemId, int _qty);
}
