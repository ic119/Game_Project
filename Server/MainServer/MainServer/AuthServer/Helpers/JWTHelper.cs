using MainServer.AuthServer.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MainServer.AuthServer.Helpers
{
    public static class JWTHelper
    {
        public static string GenerateAccessToken(User _user, IConfiguration _config)
        {
            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, _user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, _user.Username)
        };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static string GenerateRefreshToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        // DB에는 refresh token 원문이 아니라 이 해시만 저장한다 - DB가 유출돼도 저장된 값으로는 토큰을 재발급받을 수 없다.
        // 토큰 자체가 64바이트 난수라 비밀번호처럼 느린 해시(BCrypt)가 필요 없고, 같은 입력이면 같은 값이 나와야 조회할 수 있다.
        public static string HashRefreshToken(string refreshToken)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        }
    }
}
