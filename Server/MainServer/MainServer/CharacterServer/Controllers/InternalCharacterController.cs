using MainServer.CharacterServer.DTOs;
using MainServer.CharacterServer.Filters;
using MainServer.CharacterServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainServer.CharacterServer.Controllers
{
    // GameServer만 호출하는 서버 간 API. 사용자 JWT가 아니라 InternalApiKey로 보호되며, 클라이언트는 호출할 수 없다.
    // 캐릭터 소유권 검증은 여기서 하지 않는다 - GameServer가 Game_EnterRequest 시점에 AccessToken으로 이미
    // 소유권을 확인한 세션의 characterId만 넘기기 때문이다(PlayerAuthValidator 참고).
    [ApiController]
    [Route("api/internal/characters")]
    [InternalApiKey]
    public class InternalCharacterController : ControllerBase
    {
        private readonly ICharacterService _characterService;

        public InternalCharacterController(ICharacterService characterService) => _characterService = characterService;

        // GET /api/internal/characters/{characterId} — 캐릭터 조회(GameServer가 장비 변경 후 전투 스탯을 다시 계산할 때)
        [HttpGet("{characterId:long}")]
        public async Task<IActionResult> GetCharacter(long characterId)
        {
            var result = await _characterService.GetCharacterForServerAsync(characterId);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(result); // 200
        }

        // POST /api/internal/characters/{characterId}/kill-rewards — GameServer가 계산한 몬스터 처치 보상(경험치/레벨/골드/아이템) 저장
        [HttpPost("{characterId:long}/kill-rewards")]
        public async Task<IActionResult> ApplyKillRewards(long characterId, [FromBody] ApplyKillRewardsRequest request)
        {
            // 보상 id가 없으면 중복 반영을 막을 수 없다 - 모든 보상이 같은 id(빈 값)로 묶여 첫 보상 이후 전부 무시되기도 한다.
            if (request._rewardId == Guid.Empty)
                return BadRequest(new { message = "보상 id가 없습니다." }); // 400

            var result = await _characterService.ApplyKillRewardsAsync(characterId, request);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : NoContent(); // 204 - GameServer는 응답 바디를 쓰지 않는다
        }

        // POST /api/internal/characters/{characterId}/items/{itemId}/consume — 소비 아이템(물약 등) 1개 차감.
        // GameServer가 효과(회복)를 적용하기 직전에 호출한다. 보유하지 않은 아이템이면 400.
        [HttpPost("{characterId:long}/items/{itemId}/consume")]
        public async Task<IActionResult> ConsumeItem(long characterId, string itemId)
        {
            try
            {
                var result = await _characterService.ConsumeItemAsync(characterId, itemId);
                return result is null
                    ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                    : NoContent(); // 204
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message }); // 400
            }
        }
    }
}
