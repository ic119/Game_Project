using MainServer.AuthServer.Entities;

namespace MainServer.CharacterServer.Entities
{
    // 계정별 캐릭터 슬롯 현황. 현재 보유 캐릭터 수(CurrentCount)를 최대 슬롯 수(MaxSlotCount)와
    // 비교해 생성 가능 여부를 판단한다. 캐릭터는 ID 기반(api/characters/{id})으로 목록 조회/수정/삭제되므로,
    // MaxSlotCount를 조정하는 것만으로 계정당 보유 가능한 캐릭터 수를 바꿀 수 있다.
    public class CharacterSlot
    {
        public const int DefaultMaxSlotCount = 3;

        public long UserId { get; set; }
        public int MaxSlotCount { get; set; } = DefaultMaxSlotCount;
        public int CurrentCount { get; set; }
        public User User { get; set; } = null!;
    }
}
