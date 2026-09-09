using MainServer.AuthServer.Entities;

namespace MainServer.CharacterServer.Entities
{
    // 계정별 캐릭터 슬롯 현황. 다중 캐릭터 지원을 위한 준비 단계로,
    // 현재 보유 캐릭터 수(CurrentCount)를 최대 슬롯 수(MaxSlotCount)와 비교해 생성 가능 여부를 판단한다.
    // MaxSlotCount를 1보다 크게 올리려면 Character.UserId의 단일 유니크 제약(AppDbContext)과
    // 캐릭터 단건 조회 위주로 짜여진 API/클라이언트(캐릭터 ID 없이 "내 캐릭터" 하나만 다루는 구조)도
    // 함께 캐릭터 ID 기반 목록/선택 구조로 바꿔야 한다.
    public class CharacterSlot
    {
        public const int DefaultMaxSlotCount = 1;

        public long UserId { get; set; }
        public int MaxSlotCount { get; set; } = DefaultMaxSlotCount;
        public int CurrentCount { get; set; }
        public User User { get; set; } = null!;
    }
}
