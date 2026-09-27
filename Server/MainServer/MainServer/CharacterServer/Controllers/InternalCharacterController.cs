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

        // POST /api/internal/characters/{characterId}/kill-rewards — GameServer가 계산한 몬스터 처치 보상(경험치/레벨/골드/아이템) 저장
        [HttpPost("{characterId:long}/kill-rewards")]
        public async Task<IActionResult> ApplyKillRewards(long characterId, [FromBody] ApplyKillRewardsRequest request)
        {
            var result = await _characterService.ApplyKillRewardsAsync(characterId, request);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : NoContent(); // 204 - GameServer는 응답 바디를 쓰지 않는다
        }
    }
}
