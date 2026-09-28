namespace MainServer.CharacterServer.Entities
{
    // 이미 저장한 처치 보상의 id(GameServer가 보상마다 발급). GameServer는 저장 요청이 타임아웃 등으로 실패하면 같은 id로
    // 다시 보내는데, 앞선 요청이 실제로는 반영된 뒤였다면 골드/아이템이 두 번 들어간다 - 이 기록으로 같은 보상은 한 번만 반영한다.
    public class KillRewardReceipt
    {
        public Guid Id { get; set; }
        public long CharacterId { get; set; }
        public DateTime CreatedAt { get; set; }

        public Character Character { get; set; } = null!;
    }
}
