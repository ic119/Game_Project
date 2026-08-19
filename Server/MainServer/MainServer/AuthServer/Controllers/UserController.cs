using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Services;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var result = await _userService.RegisterAsync(request);
                return Ok(result); // 200
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message }); // 409
            }
        }

        // GET /api/users/{id} → 사용자 조회
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var user = await _userService.GetByIdAsync(id);
            return user is null
                ? NotFound(new { message = "사용자를 찾을 수 없습니다." }) // 404
                : Ok(new UserResponse(user.Id, user.Username, user.Nickname, user.CreatedAt)); // 200
        }
    }
}
