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
            bool exists = await _db.Characters.AnyAsync(c => c.UserId == userId);
            if (exists)
                throw new InvalidOperationException("이미 생성된 캐릭터가 있습니다.");

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

        private static CharacterResponse ToResponse(Character character) => new(
            character.Id,
            character.Nickname,
            character.HairIndex,
            character.EyeIndex,
            character.MouthIndex,
            character.Str,
            character.Agi,
            character.Intel,
            character.CreatedAt);
    }
}
