using System.Net.Http.Headers;
using System.Text.Json;
using GameServer.Combat;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace GameServer.Networking
{
    // Game_EnterRequest/Game_StatUpdateRequest로 들어온 AccessToken이 실제로 요청한 characterId(PlayerId)의
    // 소유 계정인지 MainServer(AuthServer)에 위임해 확인하고, 성공하면 응답 바디에서 전투 스탯 계산에 필요한
    // 원본 데이터(str/agi/장착 중인 아이템)도 함께 파싱해 반환한다.
    // GameServer는 JWT 서명 키나 DB에 직접 접근하지 않는다 - MainServer의 CharacterController가 이미
    // [Authorize] + FindOwnedCharacterAsync로 동일한 검증(서명/만료/소유자)을 하고 있으므로, 그 결과를
    // 그대로 재사용한다. 이렇게 하면 두 서버가 서명 키를 이중으로 들고 있다가 어긋나는 사고를 피하고,
    // AttackPower/Defense를 클라이언트 자기 보고가 아니라 DB 원본에서 직접 가져와 위조를 막을 수 있다
    // (CombatStatCalculator 참고).
    public class PlayerAuthValidator
    {
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

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

        // accessToken의 계정이 characterId를 실제로 소유하는지 확인하고, 성공하면 전투 스탯 계산에 필요한
        // 원본 데이터(str/agi/장착 중인 아이템 id 목록)를 함께 반환한다. 토큰이 비어있거나, 서명/만료가
        // 유효하지 않거나(401), 다른 계정의 캐릭터(404)거나, 확인 자체가 실패하면 null.
        public async Task<CharacterCombatSnapshot?> FetchOwnedCharacterAsync(string accessToken, long characterId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/characters/{characterId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync(ct);
                var body = JsonSerializer.Deserialize<CharacterCombatResponseBody>(json, JsonOptions);
                if (body is null)
                {
                    return null;
                }

                List<string> equippedItemIds = (body._items ?? new List<CharacterItemResponseBody>())
                    .Where(item => !string.IsNullOrEmpty(item._equipSlot))
                    .Select(item => item._itemId)
                    .ToList();

                return new CharacterCombatSnapshot(body._str, body._agi, equippedItemIds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // AuthServer에 연결할 수 없는 등 확인 자체가 실패한 경우 - 신원을 증명할 수 없으므로 안전하게 거부한다.
                Console.WriteLine($"[GameServer] AuthServer 소유권/스탯 확인 실패 : {ex.Message}");
                return null;
            }
        }

        // MainServer.CharacterServer.DTOs.CharacterResponse의 부분 집합. GameServer는 MainServer 프로젝트를
        // 참조하지 않으므로(별도 배포 단위) 전투 스탯 계산에 필요한 필드(_str/_agi/_items)만 별도로 선언해
        // 파싱한다. MainServer 쪽 DTO 필드명이 바뀌면 이 클래스도 함께 맞춰야 한다.
        private class CharacterCombatResponseBody
        {
            public int _str { get; set; }
            public int _agi { get; set; }
            public List<CharacterItemResponseBody>? _items { get; set; }
        }

        private class CharacterItemResponseBody
        {
            public string _itemId { get; set; } = string.Empty;
            public string? _equipSlot { get; set; }
        }
    }
}
