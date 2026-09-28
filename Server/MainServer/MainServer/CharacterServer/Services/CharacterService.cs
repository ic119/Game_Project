using MainServer.AuthServer.Data;
using MainServer.CharacterServer.DTOs;
using MainServer.CharacterServer.Entities;
using MainServer.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        // 서버 간 API(InternalCharacterController) 전용 조회 - 소유권 검증 없이 id로 찾는다. GameServer가 입장 시점에 이미
        // 소유권을 확인한 세션의 캐릭터 스탯을 다시 읽을 때 쓴다(사용자 AccessToken은 30분이면 만료돼 세션 중에 쓸 수 없다).
        public async Task<CharacterResponse?> GetCharacterForServerAsync(long characterId)
        {
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == characterId);
            return character is null ? null : await ToResponseAsync(character);
        }

        // 입력값이 규칙에 맞지 않으면 ArgumentException(400), 슬롯이 가득 찼거나 닉네임이 이미 쓰이면 InvalidOperationException(409).
        public async Task<CharacterResponse> CreateAsync(long userId, CreateCharacterRequest request)
        {
            string nickname = InputRules.NormalizeNickname(request._nickname);
            InputRules.ValidateCustomization(request._hairIndex, request._eyeIndex, request._mouthIndex);

            // 같은 계정의 생성/삭제를 한 번에 하나씩 처리한다 - 동시에 두 번 생성하면 둘 다 "슬롯 남음"을 보고 제한을 넘길 수 있었다.
            await using var transaction = await BeginAccountLockAsync(userId);

            var slot = await GetOrCreateSlotAsync(userId);
            if (slot.CurrentCount >= slot.MaxSlotCount)
                throw new InvalidOperationException("보유 가능한 캐릭터 슬롯을 모두 사용했습니다.");

            // 다른 플레이어 머리 위와 채팅에 이 닉네임이 그대로 보이므로, 같은 닉네임의 캐릭터가 둘 생기지 않게 한다.
            // (DB 유니크 인덱스는 두지 않았다 - 기존 데이터에 중복이 있으면 마이그레이션이 실패하므로. 동시에 같은 닉네임으로
            // 생성하는 드문 경우는 막지 못한다.)
            if (await _db.Characters.AnyAsync(c => c.Nickname == nickname))
                throw new InvalidOperationException("이미 사용 중인 닉네임입니다.");

            var character = new Character
            {
                UserId = userId,
                Nickname = nickname,
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
            await transaction.CommitAsync();

            return await ToResponseAsync(character);
        }

        public async Task<CharacterResponse?> UpdateCustomizationAsync(long userId, long characterId, UpdateCharacterCustomizationRequest request)
        {
            InputRules.ValidateCustomization(request._hairIndex, request._eyeIndex, request._mouthIndex);

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

        // GameServer가 몬스터 처치 시 계산한 경험치/레벨/골드/아이템 보상을 한 번에 저장한다.
        // progress/gold/item을 각각 별도 요청으로 쪼개면 라운드트립도 늘고 중간에 하나만 실패했을 때
        // 상태가 어긋날 수 있어, 하나의 SaveChangesAsync로 묶어 반영한다.
        // 서버 간 API(InternalCharacterController) 전용이라 userId 소유권 검증을 하지 않는다 - GameServer가
        // 입장 시점에 이미 소유권을 확인한 세션의 characterId만 넘긴다.
        // GameServer는 저장 실패(타임아웃 등) 시 같은 _rewardId로 다시 보낸다. 이미 반영한 보상이면 다시 더하지 않고 성공으로 응답한다 -
        // 보상 반영과 수령 기록(KillRewardReceipt)을 같은 SaveChangesAsync(트랜잭션)로 저장하므로 둘은 항상 함께 남거나 함께 빠진다.
        public async Task<CharacterResponse?> ApplyKillRewardsAsync(long characterId, ApplyKillRewardsRequest request)
        {
            await using var transaction = await BeginCharacterLockAsync(characterId);

            var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == characterId);
            if (character is null)
                return null;

            if (await _db.KillRewardReceipts.AnyAsync(r => r.Id == request._rewardId))
                return await ToResponseAsync(character);

            _db.KillRewardReceipts.Add(new KillRewardReceipt
            {
                Id = request._rewardId,
                CharacterId = characterId,
                CreatedAt = DateTime.UtcNow
            });

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

            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                // 재시도 요청이 앞선 요청(아직 처리 중이던)과 겹쳐, 저쪽이 먼저 같은 보상 id로 저장한 경우 - 기본 키 중복으로
                // 이번 저장은 반영되지 않았으므로 보상은 한 번만 반영됐다. 추적 중인 변경은 버리고 저장된 상태를 돌려준다.
                // (같은 캐릭터 요청은 위 잠금으로 차례로 처리되므로 보통은 앞의 수령 기록 확인에서 걸러진다.)
                // 그 밖의 저장 실패는 그대로 던진다(500 - GameServer가 다시 보낸다).
                if (!await IsKillRewardAlreadySavedAsync(request._rewardId))
                    throw;

                _db.ChangeTracker.Clear();
                character = await _db.Characters.FirstAsync(c => c.Id == characterId);
            }

            return await ToResponseAsync(character);
        }

        // 추적 중인(저장 실패한) 엔티티가 아니라 DB에 실제로 있는지 확인한다.
        private Task<bool> IsKillRewardAlreadySavedAsync(Guid rewardId) =>
            _db.KillRewardReceipts.AsNoTracking().AnyAsync(r => r.Id == rewardId);

        // 인벤토리 UI에서 장착 가능한 슬롯 이름. 클라이언트 EquipmentSlotType(None 제외)과 철자를 맞춘다.
        public static readonly IReadOnlySet<string> ValidEquipSlots = new HashSet<string> { "Weapon", "Armor", "Helmet", "Boots", "Accessory" };

        // 인벤토리 아이템을 장비 슬롯에 장착한다(소유자 검증 포함). 같은 슬롯에 이미 장착돼 있던 다른 아이템은
        // 자동으로 해제한 뒤(먼저 저장) 새 아이템을 장착한다 - (CharacterId, EquipSlot) 유니크 인덱스가 있어
        // 두 UPDATE를 한 SaveChangesAsync에 묶으면 EF가 실행 순서를 보장하지 않아 일시적으로 제약 위반이 날 수 있다.
        // 두 저장은 한 트랜잭션이라, 두 번째가 실패하면 첫 번째 해제도 되돌려진다(예전에는 기존 장비만 풀린 채 남을 수 있었다).
        public async Task<CharacterResponse?> EquipItemAsync(long userId, long characterId, EquipItemRequest request)
        {
            await using var transaction = await BeginCharacterLockAsync(characterId);

            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            if (string.IsNullOrEmpty(request._equipSlot) || !ValidEquipSlots.Contains(request._equipSlot))
                throw new InvalidOperationException($"알 수 없는 장비 슬롯입니다: {request._equipSlot}");

            var targetItem = await _db.CharacterItems
                .FirstOrDefaultAsync(ci => ci.CharacterId == characterId && ci.ItemId == request._itemId);

            if (targetItem is null)
                throw new InvalidOperationException("보유하지 않은 아이템은 장착할 수 없습니다.");

            if (!ItemEquipSlotCatalog.CanEquip(request._itemId, request._equipSlot))
                throw new InvalidOperationException("이 아이템은 해당 슬롯에 장착할 수 없습니다.");

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
            await transaction.CommitAsync();

            return await ToResponseAsync(character);
        }

        // 장비 슬롯을 해제한다(소유자 검증 포함). 해당 슬롯에 장착된 아이템이 없으면 아무 것도 하지 않고 현재 상태를 그대로 반환한다.
        public async Task<CharacterResponse?> UnequipItemAsync(long userId, long characterId, string equipSlot)
        {
            await using var transaction = await BeginCharacterLockAsync(characterId);

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

            await transaction.CommitAsync();
            return await ToResponseAsync(character);
        }

        // 소비 아이템(물약 등) 1개를 차감한다. 장착 중인 스택(EquipSlot != null)은 대상에서 제외한다 -
        // 장비류는 이 경로로 소모될 일이 없고, 혹시 잘못된 요청이 와도 장착 중인 아이템이 실수로 사라지지
        // 않도록 방어한다. 수량이 0 이하가 되면 스택 자체를 삭제한다.
        // 서버 간 API(InternalCharacterController) 전용이라 userId 소유권 검증을 하지 않는다(ApplyKillRewardsAsync와 동일).
        public async Task<CharacterResponse?> ConsumeItemAsync(long characterId, string itemId)
        {
            await using var transaction = await BeginCharacterLockAsync(characterId);

            var character = await _db.Characters.FirstOrDefaultAsync(c => c.Id == characterId);
            if (character is null)
                return null;

            var targetItem = await _db.CharacterItems
                .FirstOrDefaultAsync(ci => ci.CharacterId == characterId && ci.ItemId == itemId && ci.EquipSlot == null);

            if (targetItem is null)
                throw new InvalidOperationException("보유하지 않은 아이템은 사용할 수 없습니다.");

            targetItem.Quantity -= 1;
            if (targetItem.Quantity <= 0)
            {
                _db.CharacterItems.Remove(targetItem);
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return await ToResponseAsync(character);
        }

        // 인벤토리 아이템을 버린다(소유자 검증 포함). 장착 중인 스택(EquipSlot != null)은 대상에서 제외한다 -
        // 장비를 버리려면 먼저 해제해야 한다. DELETE 의미에 맞게 대상이 이미 없어도 에러 없이 현재 상태를 그대로 반환한다.
        public async Task<CharacterResponse?> RemoveItemAsync(long userId, long characterId, string itemId)
        {
            await using var transaction = await BeginCharacterLockAsync(characterId);

            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return null;

            var targetItem = await _db.CharacterItems
                .FirstOrDefaultAsync(ci => ci.CharacterId == characterId && ci.ItemId == itemId && ci.EquipSlot == null);

            if (targetItem is not null)
            {
                _db.CharacterItems.Remove(targetItem);
                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
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
            // 생성과 같은 계정 잠금(슬롯 수 갱신), 그다음 캐릭터 잠금(진행 중인 보상 저장/아이템 처리가 끝난 뒤 지운다) 순서로 잡는다.
            // 다른 작업은 캐릭터 잠금만 잡으므로 순서가 엇갈려 교착되지 않는다.
            await using var transaction = await BeginAccountLockAsync(userId);
            await LockCharacterRowAsync(characterId);

            var character = await FindOwnedCharacterAsync(userId, characterId);
            if (character is null)
                return false;

            _db.Characters.Remove(character);

            var slot = await GetOrCreateSlotAsync(userId);
            slot.CurrentCount = Math.Max(0, slot.CurrentCount - 1);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }

        // 캐릭터 한 명의 골드/인벤토리/장비를 바꾸는 작업은 이 잠금 안에서 한 번에 하나씩 처리한다. 트랜잭션을 열고 캐릭터 행을
        // SELECT ... FOR UPDATE로 잠그므로, 같은 캐릭터의 다른 요청은 이 트랜잭션이 끝날 때까지 기다린다.
        // 예전에는 각 요청이 "읽고 -> 메모리에서 더하고/빼고 -> 쓰기"를 따로 해서, 처치 보상 저장(GameServer 백그라운드)과 물약 차감,
        // 아이템 버리기가 겹치면 한쪽의 수량 변경이 사라지거나, 지워진 행을 갱신하려다 저장 자체가 실패했다.
        // 잠금은 이 행에만 걸리고(다른 캐릭터와는 무관) 반드시 먼저 잡은 뒤 데이터를 읽어야 최신 값을 본다.
        private async Task<IDbContextTransaction> BeginCharacterLockAsync(long characterId)
        {
            var transaction = await _db.Database.BeginTransactionAsync();
            await LockCharacterRowAsync(characterId);
            return transaction;
        }

        private Task LockCharacterRowAsync(long characterId) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM characters WHERE Id = {characterId} FOR UPDATE");

        // 계정 단위(캐릭터 생성/삭제 - 슬롯 수)로 한 번에 하나씩 처리하기 위한 잠금. 계정 행을 잠근다.
        private async Task<IDbContextTransaction> BeginAccountLockAsync(long userId)
        {
            var transaction = await _db.Database.BeginTransactionAsync();
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM users WHERE Id = {userId} FOR UPDATE");
            return transaction;
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
