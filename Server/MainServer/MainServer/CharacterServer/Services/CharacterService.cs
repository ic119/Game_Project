using MainServer.AuthServer.Data;
using MainServer.CharacterServer.DTOs;
using MainServer.CharacterServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace MainServer.CharacterServer.Services
{
    public class CharacterService : ICharacterService
    {
        // 캐릭터 생성 시 부여되는 기본 능력치. Client의 UserStats.CreateDefault()와 동일한 값으로 맞춘다.
        private const int DefaultStat = 5;

        private readonly AppDbContext _db;

        public CharacterService(AppDbContext db) => _db = db;

        public async Task<CharacterResponse?> GetMyCharacterAsync(long userId)
        {
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.UserId == userId);
            return character is null ? null : ToResponse(character);
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

            return ToResponse(character);
        }

        public async Task<CharacterResponse?> UpdateCustomizationAsync(long userId, UpdateCharacterCustomizationRequest request)
        {
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.UserId == userId);
            if (character is null)
                return null;

            character.HairIndex = request._hairIndex;
            character.EyeIndex = request._eyeIndex;
            character.MouthIndex = request._mouthIndex;
            character.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return ToResponse(character);
        }

        public async Task<bool> DeleteMyCharacterAsync(long userId)
        {
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.UserId == userId);
            if (character is null)
                return false;

            _db.Characters.Remove(character);

            var slot = await GetOrCreateSlotAsync(userId);
            slot.CurrentCount = Math.Max(0, slot.CurrentCount - 1);

            await _db.SaveChangesAsync();

            return true;
        }

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

        private static CharacterResponse ToResponse(Character character) => new(
            character.Id,
            character.Nickname,
            character.HairIndex,
            character.EyeIndex,
            character.MouthIndex,
            character.Str,
            character.Agi,
            character.Intel,
            character.Level,
            character.LastLoginAt,
            character.CreatedAt);
    }
}
