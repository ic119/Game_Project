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
