using MainServer.AuthServer.Data;
using MainServer.CharacterServer.DTOs;
using MainServer.CharacterServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace MainServer.CharacterServer.Services
{
    public class CharacterService : ICharacterService
    {
        // 캐릭터 생성 시 부여되는 기본 능력치. Client의 UserStats.CreateDefault()(10/10/10)와 동일한 값으로 맞춘다.
        // 몬스터 스탯(Monsters/몬스터_밸런싱_공식.txt)도 이 기준값(str=10 → AttackPower=10)을 전제로 계산되어 있으므로,
        // 이 값이 달라지면 밸런싱 문서의 DangerScore/ExpReward 역산이 전부 어긋난다.
        private const int DefaultStat = 10;

        private readonly AppDbContext _db;

        public CharacterService(AppDbContext db) => _db = db;

        public async Task<IReadOnlyList<CharacterResponse>> GetMyCharactersAsync(long userId)
        {
            var characters = await _db.Characters.Where(c => c.UserId == userId).ToListAsync();

            var result = new List<CharacterResponse>(characters.Count);
            foreach (var character in characters)
            {
                result.Add(await ToResponseAsync(character));
            }
            return result;
        }

        public async Task<CharacterResponse?> GetCharacterAsync(long userId, long characterId)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            return character is null ? null : await ToResponseAsync(character);
        }

        public async Task<CharacterResponse> CreateAsync(long userId, CreateCharacterRequest request)
        {
            var slot = await GetOrCreateSlotAsync(userId);
            if (slot.CurrentCount >= slot.MaxSlotCount)
                throw new InvalidOperationException("보유 가능한 캐릭터 슬롯을 모두 사용했습니다.");

            var character = new Character
            {
                UserId = userId,
                Nickname = request._nickname,
                HairIndex = request._hairIndex,
                EyeIndex = request._eyeIndex,
                MouthIndex = request._mouthIndex,
                Str = DefaultStat,
                Agi = DefaultStat,
                Intel = DefaultStat,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Characters.Add(character);
            slot.CurrentCount++;
            await _db.SaveChangesAsync();

            return await ToResponseAsync(character);
        }

        public async Task<CharacterResponse?> UpdateCustomizationAsync(long userId, long characterId, UpdateCharacterCustomizationRequest request)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            character.HairIndex = request._hairIndex;
            character.EyeIndex = request._eyeIndex;
            character.MouthIndex = request._mouthIndex;
            character.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return await ToResponseAsync(character);
        }

        // GameServer(TCP)가 몬스터 처치 시 계산한 레벨/경험치를 클라이언트가 대신 저장 요청한다.
        // GameServer 자체는 DB 접근 권한이 없어 이 경로를 거쳐야만 영속화된다.
        public async Task<CharacterResponse?> UpdateProgressAsync(long userId, long characterId, UpdateCharacterProgressRequest request)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            character.Level = request._level;
            character.Exp = request._exp;
            character.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return await ToResponseAsync(character);
        }

        // GameServer가 몬스터 처치 시 계산한 경험치/레벨/골드/아이템 보상을 한 번에 저장한다.
        // progress/gold/item을 각각 별도 요청으로 쪼개면 라운드트립도 늘고 중간에 하나만 실패했을 때
        // 클라이언트-서버 상태가 어긋날 수 있어, 하나의 SaveChangesAsync로 묶어 반영한다.
        public async Task<CharacterResponse?> ApplyKillRewardsAsync(long userId, long characterId, ApplyKillRewardsRequest request)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            character.Level = request._level;
            character.Exp = request._exp;
            character.Gold += request._goldGained;
            character.UpdatedAt = DateTime.UtcNow;

            foreach (var item in request._items)
            {
                if (item._qty <= 0)
                    continue;

                var existing = await _db.CharacterItems
                    .FirstOrDefaultAsync(ci => ci.CharacterId == characterId && ci.ItemId == item._itemId);

                if (existing is null)
                {
                    _db.CharacterItems.Add(new CharacterItem
                    {
                        CharacterId = characterId,
                        ItemId = item._itemId,
                        Quantity = item._qty
                    });
                }
                else
                {
                    existing.Quantity += item._qty;
                }
            }

            await _db.SaveChangesAsync();

            return await ToResponseAsync(character);
        }

        // 인벤토리 UI에서 장착 가능한 슬롯 이름. 클라이언트 EquipmentSlotType(None 제외)과 철자를 맞춘다.
        private static readonly HashSet<string> ValidEquipSlots = new() { "Weapon", "Armor", "Helmet", "Boots", "Accessory" };

        // 인벤토리 아이템을 장비 슬롯에 장착한다(소유자 검증 포함). 같은 슬롯에 이미 장착돼 있던 다른 아이템은
        // 자동으로 해제한 뒤(먼저 커밋) 새 아이템을 장착한다 - (CharacterId, EquipSlot) 유니크 인덱스가 있어
        // 두 UPDATE를 한 SaveChangesAsync에 묶으면 EF가 실행 순서를 보장하지 않아 일시적으로 제약 위반이 날 수 있다.
        public async Task<CharacterResponse?> EquipItemAsync(long userId, long characterId, EquipItemRequest request)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            if (string.IsNullOrEmpty(request._equipSlot) || !ValidEquipSlots.Contains(request._equipSlot))
                throw new InvalidOperationException($"알 수 없는 장비 슬롯입니다: {request._equipSlot}");

            var targetItem = await _db.CharacterItems
                .FirstOrDefaultAsync(ci => ci.CharacterId == characterId && ci.ItemId == request._itemId);

            if (targetItem is null)
                throw new InvalidOperationException("보유하지 않은 아이템은 장착할 수 없습니다.");

            if (targetItem.EquipSlot == request._equipSlot)
                return await ToResponseAsync(character); // 이미 장착 중

            var previouslyEquipped = await _db.CharacterItems
                .Where(ci => ci.CharacterId == characterId && ci.EquipSlot == request._equipSlot)
                .ToListAsync();

            if (previouslyEquipped.Count > 0)
            {
                foreach (var item in previouslyEquipped)
                {
                    item.EquipSlot = null;
                }
                await _db.SaveChangesAsync();
            }

            targetItem.EquipSlot = request._equipSlot;
            await _db.SaveChangesAsync();

            return await ToResponseAsync(character);
        }

        // 장비 슬롯을 해제한다(소유자 검증 포함). 해당 슬롯에 장착된 아이템이 없으면 아무 것도 하지 않고 현재 상태를 그대로 반환한다.
        public async Task<CharacterResponse?> UnequipItemAsync(long userId, long characterId, string equipSlot)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            var equippedItem = await _db.CharacterItems
                .FirstOrDefaultAsync(ci => ci.CharacterId == characterId && ci.EquipSlot == equipSlot);

            if (equippedItem is not null)
            {
                equippedItem.EquipSlot = null;
                await _db.SaveChangesAsync();
            }

            return await ToResponseAsync(character);
        }

        // 로그아웃 시점에 서버 시각(UtcNow) 기준으로 마지막 접속시간을 기록한다. 클라이언트 시각을 신뢰하지 않는다.
        public async Task<CharacterResponse?> TouchLastLoginAsync(long userId, long characterId)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            character.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return await ToResponseAsync(character);
        }

        public async Task<bool> DeleteCharacterAsync(long userId, long characterId)
        {
            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return false;

            _db.Characters.Remove(character);

            var slot = await GetOrCreateSlotAsync(userId);
            slot.CurrentCount = Math.Max(0, slot.CurrentCount - 1);

            await _db.SaveChangesAsync();

            return true;
        }

        // characterId가 실제로 이 계정(userId) 소유인지까지 함께 검증한다. 다중 캐릭터 환경에서
        // 다른 계정의 캐릭터 ID를 넘겨 조회/수정/삭제하는 것을 막기 위한 필수 검증이다.
        private async Task<Character?> FindOwnedCharacterAsync(long userId, long characterId) =>
            await _db.Characters.FirstOrDefaultAsync(c => c.Id == characterId && c.UserId == userId);

        // 슬롯 정보가 없는 계정(마이그레이션 이전 가입자 등)을 위해 최초 조회 시 기본값으로 지연 생성한다.
        private async Task<CharacterSlot> GetOrCreateSlotAsync(long userId)
        {
            var slot = await _db.CharacterSlots.FirstOrDefaultAsync(s => s.UserId == userId);
            if (slot is not null)
                return slot;

            slot = new CharacterSlot { UserId = userId };
            _db.CharacterSlots.Add(slot);

            return slot;
        }

        // 인벤토리 아이템(CharacterItems)까지 함께 실어 보내는 응답 빌더. 목록/생성/진행도 저장 등
        // CharacterResponse를 만드는 모든 경로가 이 메서드 하나를 거치므로, 클라이언트는 어느 API를 호출하든
        // 항상 최신 아이템 목록을 함께 받는다 - 별도의 "인벤토리 조회 API"를 새로 만들 필요가 없다.
        private async Task<CharacterResponse> ToResponseAsync(Character character)
        {
            var items = await _db.CharacterItems
                .Where(ci => ci.CharacterId == character.Id)
                .Select(ci => new CharacterItemResponse(ci.ItemId, ci.Quantity, ci.EquipSlot))
                .ToListAsync();

            return new CharacterResponse(
                character.Id,
                character.Nickname,
                character.HairIndex,
                character.EyeIndex,
                character.MouthIndex,
                character.Str,
                character.Agi,
                character.Intel,
                character.Level,
                character.Exp,
                character.Gold,
                items,
                character.LastLoginAt,
                character.CreatedAt);
        }
    }
}
