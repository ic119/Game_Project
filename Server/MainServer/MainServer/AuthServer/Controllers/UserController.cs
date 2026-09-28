using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace MainServer.AuthServer.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService) => _userService = userService;

        // POST /api/users/register → 회원가입
        [HttpPost("register")]
        [EnableRateLimiting(RateLimitPolicies.Auth)] // IP당 요청 수 제한(Program.cs) - 계정 대량 생성 방지
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var result = await _userService.RegisterAsync(request);
                return Ok(result); // 200
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message }); // 400 - 입력값 규칙 위반(InputRules)
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message }); // 409
            }
        }

        // GET /api/users/me → 로그인한 본인 계정 조회. 예전에는 GET /api/users/{id}로 로그인 없이 아무 계정의 아이디를
        // 조회할 수 있어, id를 1부터 늘려 가며 가입된 아이디 목록을 모을 수 있었다(비밀번호 대입의 표적 수집).
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMe()
        {
            long userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _userService.GetByIdAsync(userId);
            return user is null
                ? NotFound(new { message = "사용자를 찾을 수 없습니다." }) // 404
                : Ok(new UserResponse(user.Id, user.Username, user.Nickname, user.CreatedAt)); // 200
        }
    }
}
