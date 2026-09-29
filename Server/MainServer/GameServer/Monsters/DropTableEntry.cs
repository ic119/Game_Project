namespace GameServer.Monsters
{
    // 드롭 테이블 한 항목. Roll마다 독립적으로 DropRate를 굴려 당첨 여부를 판정한다(가중치 1개 뽑기가
    // 아니라 항목별 독립 확률이라, 한 번에 여러 아이템이 동시에 나올 수 있다 - DropTableCatalog.Roll 참고).
    public class DropTableEntry
    {
        // ItemId 또는 Grade 중 정확히 하나만 채운다(DropTableCatalog.EnsureLoaded가 부팅 시 검증한다).
        // ItemId를 채우면 그 아이템 고정, Grade를 채우면 그 등급("Common"/"Rare"/"Epic"/"Legendary")에 속한
        // 아이템 중 하나를 Roll마다 무작위로 고른다(ItemCatalog.TryGetRandomByGrade).
        public string ItemId { get; init; } = string.Empty;
        public string? Grade { get; init; }

        public int MinQty { get; init; } = 1;
        public int MaxQty { get; init; } = 1;

        // 0.0~1.0. 1.0이면 항상 드롭(예: 필드 몬스터의 기본 재료).
        public float DropRate { get; init; }
    }
}
