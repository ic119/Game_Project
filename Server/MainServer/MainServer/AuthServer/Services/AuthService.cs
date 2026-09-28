using MainServer.AuthServer.Data;
using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Entities;
using MainServer.AuthServer.Helpers;
using Microsoft.EntityFrameworkCore;

namespace MainServer.AuthServer.Services
{
    public class AuthService : IAuthService
    {
        // 없는 아이디로 로그인할 때도 비밀번호 검증(BCrypt)에 드는 시간만큼 쓰게 하는 더미 해시. 그렇지 않으면 없는 아이디는
        // 즉시, 있는 아이디는 느리게 실패해서 응답 시간만으로 가입된 아이디를 알아낼 수 있다.
        private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing");

        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly LoginAttemptTracker _loginAttempts;

        public AuthService(AppDbContext db, IConfiguration config, LoginAttemptTracker loginAttempts)
        {
            _db = db;
            _config = config;
            _loginAttempts = loginAttempts;
        }

        // 아이디/비밀번호가 틀리면 null. 연속 실패로 잠긴 계정이면 LoginLockedException(429).
        public async Task<LoginResponse?> LoginAsync(LoginRequest request)
        {
            string username = request._username ?? string.Empty;
            if (_loginAttempts.IsLocked(username))
                throw new LoginLockedException();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            bool passwordMatches = BCrypt.Net.BCrypt.Verify(request._password ?? string.Empty, user?.PasswordHash ?? DummyPasswordHash);
            if (user is null || !passwordMatches)
            {
                _loginAttempts.RecordFailure(username);
                return null;
            }

            _loginAttempts.RecordSuccess(username);

            // 캐릭터별 "마지막 접속시간" 갱신. 캐릭터 선택 단계가 아직 없어 계정 로그인 시점을 기준으로 삼는다.
            // SaveChangesAsync는 아래 IssueTokensAsync 내부(RefreshToken 저장 시)에서 함께 호출된다.
            var character = await _db.Characters.FirstOrDefaultAsync(c => c.UserId == user.Id);
            if (character is not null)
                character.LastLoginAt = DateTime.UtcNow;

            return await IssueTokensAsync(user);
        }

        // refresh token 로테이션: 받은 토큰을 폐기하고 새 토큰을 발급한다. 이미 폐기된 토큰이 다시 오면 탈취된 것으로 본다 -
        // 정상 클라이언트는 새 토큰으로 바꿔 쓰므로 옛 토큰을 다시 보낼 일이 없다(클라이언트는 재발급을 동시에 하나만 보낸다).
        // 그 계정의 모든 refresh token을 폐기해, 훔친 쪽과 원래 사용자 모두 다시 로그인하게 한다.
        public async Task<LoginResponse?> RefreshAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return null;

            string tokenHash = JWTHelper.HashRefreshToken(refreshToken);
            var existing = await _db.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

            if (existing is null || existing.ExpiresAt < DateTime.UtcNow)
                return null;

            // 폐기 여부 확인과 폐기를 조건부 UPDATE 하나로 한다 - 같은 토큰으로 동시에 두 요청이 와도 하나만 성공한다
            // (예전에는 둘 다 "폐기 안 됨"을 보고 새 토큰을 각각 받아 갈 수 있었다).
            int revoked = await _db.RefreshTokens
                .Where(rt => rt.Id == existing.Id && !rt.IsRevoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.IsRevoked, true));

            if (revoked == 0)
            {
                await _db.RefreshTokens
                    .Where(rt => rt.UserId == existing.UserId && !rt.IsRevoked)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.IsRevoked, true));

                Console.WriteLine($"[MainServer] 폐기된 refresh token 재사용 감지 - 계정의 모든 토큰을 폐기합니다 (UserId={existing.UserId})");
                return null;
            }

            return await IssueTokensAsync(existing.User);
        }

        public async Task LogoutAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return;

            string tokenHash = JWTHelper.HashRefreshToken(refreshToken);
            await _db.RefreshTokens
                .Where(rt => rt.Token == tokenHash && !rt.IsRevoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.IsRevoked, true));
        }

        private async Task<LoginResponse> IssueTokensAsync(User user)
        {
            var now = DateTime.UtcNow;
            var accessToken = JWTHelper.GenerateAccessToken(user, _config);
            var refreshToken = JWTHelper.GenerateRefreshToken();

            // 만료된 토큰은 더 쓸 데가 없으니 지운다(로그인/재발급마다 한 행씩 쌓이기만 했다). 폐기됐지만 아직 만료 전인
            // 토큰은 남겨 둔다 - 재사용 감지(RefreshAsync)에 필요하다.
            await _db.RefreshTokens
                .Where(rt => rt.UserId == user.Id && rt.ExpiresAt < now)
                .ExecuteDeleteAsync();

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = JWTHelper.HashRefreshToken(refreshToken),
                ExpiresAt = now + RefreshTokenLifetime,
                IsRevoked = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();

            var userResponse = new UserResponse(user.Id, user.Username, user.Nickname, user.CreatedAt);
            return new LoginResponse(accessToken, refreshToken, userResponse);
        }
    }
}
