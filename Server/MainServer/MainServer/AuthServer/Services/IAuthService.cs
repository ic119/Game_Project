using MainServer.AuthServer.DTOs;

namespace MainServer.AuthServer.Services
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);
        Task<LoginResponse?> RefreshAsync(string refreshToken);
        Task LogoutAsync(string refreshToken);
    }
}
