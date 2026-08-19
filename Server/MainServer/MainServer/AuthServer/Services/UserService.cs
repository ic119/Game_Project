using MainServer.AuthServer.Data;
using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace MainServer.AuthServer.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _db;

        public UserService(AppDbContext db) => _db = db;

        public async Task<UserResponse> RegisterAsync(RegisterRequest request)
        {
            bool exists = await _db.Users.AnyAsync(u => u.Username == request._userName);
            if (exists)
                throw new InvalidOperationException("이미 존재하는 아이디입니다.");

            var user = new User
            {
                Username = request._userName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request._password),
                Nickname = request._nickName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return new UserResponse(user.Id, user.Username, user.Nickname, user.CreatedAt);
        }

        public Task<User?> GetByIdAsync(long id) =>
            _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }
}
