using MainServer.CharacterServer.DTOs;

namespace MainServer.CharacterServer.Services
{
    public interface ICharacterService
    {
        Task<CharacterResponse?> GetMyCharacterAsync(long userId);
        Task<CharacterResponse> CreateAsync(long userId, CreateCharacterRequest request);
        Task<CharacterResponse?> UpdateCustomizationAsync(long userId, UpdateCharacterCustomizationRequest request);
        Task<bool> DeleteMyCharacterAsync(long userId);
    }
}
