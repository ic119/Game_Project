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

        // GET /api/characters/me → 캐릭터 존재 여부 확인 + 조회
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var character = await _characterService.GetMyCharacterAsync(GetUserId());
            return character is null
                ? NotFound(new { message = "생성된 캐릭터가 없습니다." }) // 404
                : Ok(character); // 200
        }

        // POST /api/characters → 캐릭터 생성(최초 커스터마이징 저장)
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

        // PUT /api/characters/me → 커스터마이징 수정
        [HttpPut("me")]
        public async Task<IActionResult> UpdateCustomization([FromBody] UpdateCharacterCustomizationRequest request)
        {
            var result = await _characterService.UpdateCustomizationAsync(GetUserId(), request);
            return result is null
                ? NotFound(new { message = "생성된 캐릭터가 없습니다." }) // 404
                : Ok(result); // 200
        }

        // DELETE /api/characters/me → 캐릭터 삭제
        [HttpDelete("me")]
        public async Task<IActionResult> Delete()
        {
            bool deleted = await _characterService.DeleteMyCharacterAsync(GetUserId());
            return deleted
                ? NoContent() // 204
                : NotFound(new { message = "생성된 캐릭터가 없습니다." }); // 404
        }

        private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
