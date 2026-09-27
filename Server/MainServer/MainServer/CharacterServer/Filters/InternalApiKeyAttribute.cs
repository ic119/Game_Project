using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;

namespace MainServer.CharacterServer.Filters
{
    // GameServer -> MainServer 서버 간 호출 전용 API(InternalCharacterController)를 보호한다.
    // 사용자 JWT가 아니라 두 서버만 공유하는 InternalApi:Key를 X-Internal-Api-Key 헤더로 받아 검증한다 -
    // 처치 보상처럼 "클라이언트가 값을 정하면 안 되는" 쓰기 작업을 클라이언트가 직접 호출하지 못하게 하기 위함이다.
    // 배포 시에는 Caddy가 /api/internal/* 를 외부에 노출하지 않으므로(Caddyfile 참고) 이 검증은 2차 방어선이다.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class InternalApiKeyAttribute : Attribute, IAuthorizationFilter
    {
        public const string HeaderName = "X-Internal-Api-Key";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            string? expectedKey = configuration["InternalApi:Key"];

            if (string.IsNullOrEmpty(expectedKey)
                || !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var providedKey)
                || !FixedTimeEquals(providedKey.ToString(), expectedKey))
            {
                context.Result = new UnauthorizedResult(); // 401
            }
        }

        // 문자열 비교에 걸리는 시간으로 키를 한 글자씩 추측하는 타이밍 공격을 막기 위해 고정 시간 비교를 쓴다.
        private static bool FixedTimeEquals(string provided, string expected) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }
}
