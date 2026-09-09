using MainServer.CharacterServer.DTOs;
using MainServer.CharacterServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MainServer.CharacterServer.Controllers
{
    [ApiController]
    [Route("api/characters")]
    [Authorize]
    public class CharacterController : ControllerBase
    {
        private readonly ICharacterService _characterService;

        public CharacterController(ICharacterService characterService) => _characterService = characterService;

        // GET /api/characters — 이 계정이 보유한 캐릭터 전체 목록 조회
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var characters = await _characterService.GetMyCharactersAsync(GetUserId());
            return Ok(characters); // 200
        }

        // GET /api/characters/{characterId} — 캐릭터 단건 조회(소유자 검증 포함)
        [HttpGet("{characterId:long}")]
        public async Task<IActionResult> GetOne(long characterId)
        {
            var character = await _characterService.GetCharacterAsync(GetUserId(), characterId);
            return character is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(character); // 200
        }

        // POST /api/characters — 캐릭터 생성(최초 커스터마이징 값). 슬롯이 가득 찼으면 409
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateCharacterRequest request)
        {
            try
            {
                var result = await _characterService.CreateAsync(GetUserId(), request);
                return Ok(result); // 200
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message }); // 409
            }
        }

        // PUT /api/characters/{characterId} — 커스터마이징 수정(소유자 검증 포함)
        [HttpPut("{characterId:long}")]
        public async Task<IActionResult> UpdateCustomization(long characterId, [FromBody] UpdateCharacterCustomizationRequest request)
        {
            var result = await _characterService.UpdateCustomizationAsync(GetUserId(), characterId, request);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(result); // 200
        }

        // DELETE /api/characters/{characterId} — 캐릭터 삭제(소유자 검증 포함)
        [HttpDelete("{characterId:long}")]
        public async Task<IActionResult> Delete(long characterId)
        {
            bool deleted = await _characterService.DeleteCharacterAsync(GetUserId(), characterId);
            return deleted
                ? NoContent() // 204
                : NotFound(new { message = "캐릭터를 찾을 수 없습니다." }); // 404
        }

        private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
