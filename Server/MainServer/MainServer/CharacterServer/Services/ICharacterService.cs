using MainServer.CharacterServer.DTOs;

namespace MainServer.CharacterServer.Services
{
    public interface ICharacterService
    {
        Task<IReadOnlyList<CharacterResponse>> GetMyCharactersAsync(long userId);
        Task<CharacterResponse?> GetCharacterAsync(long userId, long characterId);
        Task<CharacterResponse> CreateAsync(long userId, CreateCharacterRequest request);
        Task<CharacterResponse?> UpdateCustomizationAsync(long userId, long characterId, UpdateCharacterCustomizationRequest request);
        Task<CharacterResponse?> UpdateProgressAsync(long userId, long characterId, UpdateCharacterProgressRequest request);
        Task<CharacterResponse?> ApplyKillRewardsAsync(long userId, long characterId, ApplyKillRewardsRequest request);
        Task<CharacterResponse?> EquipItemAsync(long userId, long characterId, EquipItemRequest request);
        Task<CharacterResponse?> UnequipItemAsync(long userId, long characterId, string equipSlot);
        Task<CharacterResponse?> ConsumeItemAsync(long userId, long characterId, string itemId);
        Task<CharacterResponse?> RemoveItemAsync(long userId, long characterId, string itemId);
        Task<CharacterResponse?> TouchLastLoginAsync(long userId, long characterId);
        Task<bool> DeleteCharacterAsync(long userId, long characterId);
    }
}
