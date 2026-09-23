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

        // PUT /api/characters/{characterId}/progress — 레벨/경험치 저장(GameServer가 계산한 값을 클라이언트가 대신 요청, 소유자 검증 포함)
        [HttpPut("{characterId:long}/progress")]
        public async Task<IActionResult> UpdateProgress(long characterId, [FromBody] UpdateCharacterProgressRequest request)
        {
            var result = await _characterService.UpdateProgressAsync(GetUserId(), characterId, request);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(result); // 200
        }

        // POST /api/characters/{characterId}/kill-rewards — 몬스터 처치 보상(경험치/레벨/골드/아이템) 일괄 저장
        // (소유자 검증 포함). 델타(골드/아이템)를 더하는 동작이라 progress와 달리 PUT이 아닌 POST를 쓴다.
        [HttpPost("{characterId:long}/kill-rewards")]
        public async Task<IActionResult> ApplyKillRewards(long characterId, [FromBody] ApplyKillRewardsRequest request)
        {
            var result = await _characterService.ApplyKillRewardsAsync(GetUserId(), characterId, request);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(result); // 200
        }

        // PUT /api/characters/{characterId}/equipment — 인벤토리 아이템을 장비 슬롯에 장착(소유자 검증 포함)
        [HttpPut("{characterId:long}/equipment")]
        public async Task<IActionResult> EquipItem(long characterId, [FromBody] EquipItemRequest request)
        {
            try
            {
                var result = await _characterService.EquipItemAsync(GetUserId(), characterId, request);
                return result is null
                    ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                    : Ok(result); // 200
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message }); // 400
            }
        }

        // DELETE /api/characters/{characterId}/equipment/{equipSlot} — 장비 슬롯 해제(소유자 검증 포함)
        [HttpDelete("{characterId:long}/equipment/{equipSlot}")]
        public async Task<IActionResult> UnequipItem(long characterId, string equipSlot)
        {
            var result = await _characterService.UnequipItemAsync(GetUserId(), characterId, equipSlot);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(result); // 200
        }

        // POST /api/characters/{characterId}/items/{itemId}/consume — 소비 아이템(물약 등) 1개 사용(수량 차감, 소유자 검증 포함)
        [HttpPost("{characterId:long}/items/{itemId}/consume")]
        public async Task<IActionResult> ConsumeItem(long characterId, string itemId)
        {
            try
            {
                var result = await _characterService.ConsumeItemAsync(GetUserId(), characterId, itemId);
                return result is null
                    ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                    : Ok(result); // 200
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message }); // 400
            }
        }

        // DELETE /api/characters/{characterId}/items/{itemId} — 인벤토리 아이템 버리기(장착 중이 아닌 스택 전체 제거, 소유자 검증 포함)
        [HttpDelete("{characterId:long}/items/{itemId}")]
        public async Task<IActionResult> RemoveItem(long characterId, string itemId)
        {
            var result = await _characterService.RemoveItemAsync(GetUserId(), characterId, itemId);
            return result is null
                ? NotFound(new { message = "캐릭터를 찾을 수 없습니다." }) // 404
                : Ok(result); // 200
        }

        // PUT /api/characters/{characterId}/last-login — 로그아웃 시점의 마지막 접속시간을 서버 시각 기준으로 기록(소유자 검증 포함)
        [HttpPut("{characterId:long}/last-login")]
        public async Task<IActionResult> TouchLastLogin(long characterId)
        {
            var result = await _characterService.TouchLastLoginAsync(GetUserId(), characterId);
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
