using MainServer.AuthServer.Data;
using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Entities;
using MainServer.AuthServer.Helpers;
using Microsoft.EntityFrameworkCore;

namespace MainServer.AuthServer.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public AuthService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<LoginResponse?> LoginAsync(LoginRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request._username);
            if (user is null || !BCrypt.Net.BCrypt.Verify(request._password, user.PasswordHash))
                return null;

            // 캐릭터별 "마지막 접속시간" 갱신. 캐릭터 선택 단계가 아직 없어 계정 로그인 시점을 기준으로 삼는다.
            // SaveChangesAsync는 아래 IssueTokensAsync 내부(RefreshToken 저장 시)에서 함께 호출된다.
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.UserId == user.Id);
            if (character is not null)
                character.LastLoginAt = DateTime.UtcNow;

            return await IssueTokensAsync(user);
        }

        public async Task<LoginResponse?> RefreshAsync(string refreshToken)
        {
            var existing = await _db.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (existing is null || existing.IsRevoked || existing.ExpiresAt < DateTime.UtcNow)
                return null;

            existing.IsRevoked = true;
            return await IssueTokensAsync(existing.User);
        }

        public async Task LogoutAsync(string refreshToken)
        {
            var existing = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
            if (existing is null)
                return;

            existing.IsRevoked = true;
            await _db.SaveChangesAsync();
        }

        private async Task<LoginResponse> IssueTokensAsync(User user)
        {
            var accessToken = JWTHelper.GenerateAccessToken(user, _config);
            var refreshToken = JWTHelper.GenerateRefreshToken();

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                IsRevoked = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            var userResponse = new UserResponse(user.Id, user.Username, user.Nickname, user.CreatedAt);
            return new LoginResponse(accessToken, refreshToken, userResponse);
        }
    }
}
