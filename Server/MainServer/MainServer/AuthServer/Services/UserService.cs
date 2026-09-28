using MainServer.AuthServer.Data;
using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Entities;
using MainServer.Validation;
using Microsoft.EntityFrameworkCore;

namespace MainServer.AuthServer.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _db;

        public UserService(AppDbContext db) => _db = db;

        // 입력값이 규칙에 맞지 않으면 ArgumentException(400), 아이디가 이미 있으면 InvalidOperationException(409).
        public async Task<UserResponse> RegisterAsync(RegisterRequest request)
        {
            InputRules.ValidateUsername(request._userName);
            InputRules.ValidatePassword(request._password);
            string nickname = InputRules.NormalizeNickname(request._nickname);

            bool exists = await _db.Users.AnyAsync(u => u.Username == request._userName);
            if (exists)
                throw new InvalidOperationException("이미 존재하는 아이디입니다.");

            var user = new User
            {
                Username = request._userName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request._password),
                Nickname = nickname,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 같은 아이디로 동시에 가입해 위 확인을 둘 다 통과한 경우 - 아이디 유니크 인덱스에 걸린 쪽이다(예전에는 500).
                if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == request._userName))
                    throw new InvalidOperationException("이미 존재하는 아이디입니다.");

                throw;
            }

            return new UserResponse(user.Id, user.Username, user.Nickname, user.CreatedAt);
        }

        public Task<User?> GetByIdAsync(long id) =>
            _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }
}
