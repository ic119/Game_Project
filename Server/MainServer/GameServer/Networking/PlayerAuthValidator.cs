using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    // Game_EnterRequest로 들어온 AccessToken이 실제로 요청한 characterId(PlayerId)의 소유 계정인지
    // MainServer(AuthServer)에 위임해 확인한다.
    // GameServer는 JWT 서명 키나 DB에 직접 접근하지 않는다 - MainServer의 CharacterController가 이미
    // [Authorize] + FindOwnedCharacterAsync로 동일한 검증(서명/만료/소유자)을 하고 있으므로, 그 결과
    // (HTTP 상태 코드)를 그대로 재사용한다. 이렇게 하면 두 서버가 서명 키를 이중으로 들고 있다가
    // 어긋나는 사고를 피할 수 있다.
    public class PlayerAuthValidator
    {
        private readonly HttpClient _httpClient;

        public PlayerAuthValidator(IConfiguration configuration)
        {
            string authServerBaseUrl = configuration["AuthServer:BaseUrl"]
                ?? throw new InvalidOperationException("AuthServer:BaseUrl 설정이 없습니다.");

            var handler = new HttpClientHandler();
#if DEBUG
            // 로컬 개발 AuthServer(dotnet dev-certs https --trust)의 자체 서명 인증서를 신뢰하기 위한 우회.
            // Client의 ServerConnectManager.LocalDevCertificateHandler와 동일한 이유이며, 동일하게
            // 프로덕션 빌드(Release)에는 포함되지 않는다.
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
#endif
            _httpClient = new HttpClient(handler) { BaseAddress = new Uri(authServerBaseUrl) };
        }

        // accessToken의 계정이 characterId를 실제로 소유하는지 확인한다.
        // 토큰이 비어있거나, 서명/만료가 유효하지 않거나(401), 다른 계정의 캐릭터(404)면 false.
        public async Task<bool> OwnsCharacterAsync(string accessToken, long characterId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/characters/{characterId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // AuthServer에 연결할 수 없는 등 확인 자체가 실패한 경우 - 신원을 증명할 수 없으므로 안전하게 거부한다.
                Console.WriteLine($"[GameServer] AuthServer 소유권 확인 실패 : {ex.Message}");
                return false;
            }
        }
    }
}
