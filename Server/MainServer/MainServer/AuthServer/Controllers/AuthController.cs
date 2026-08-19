using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainServer.AuthServer.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService) => _authService = authService;

        // POST /api/auth/login → 로그인
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _authService.LoginAsync(request);
            return result is null
                ? Unauthorized(new { message = "아이디 또는 비밀번호가 올바르지 않습니다." }) // 401
                : Ok(result); // 200
        }

        // POST /api/auth/refresh → 토큰 재발급
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            var result = await _authService.RefreshAsync(request._refreshToken);
            return result is null
                ? Unauthorized(new { message = "유효하지 않거나 만료된 토큰입니다." })
                : Ok(result);
        }

        // POST /api/auth/logout → 로그아웃
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
        {
            await _authService.LogoutAsync(request._refreshToken);
            return NoContent(); // 204
        }
    }
}
