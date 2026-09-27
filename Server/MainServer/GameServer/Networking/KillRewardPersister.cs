using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    // 몬스터 처치 보상(경험치/레벨/골드/아이템)을 MainServer의 서버 간 API(api/internal/characters/{id}/kill-rewards)로
    // 직접 저장한다. 예전에는 클라이언트가 Game_ExpGainBroadcast/Game_LootBroadcast를 받아 MainServer에 대신 저장을
    // 요청했는데, 그 API가 요청 바디의 값을 그대로 저장했기 때문에 클라이언트가 임의의 골드/아이템/레벨을 보낼 수 있었다.
    // 이제 보상 값은 GameServer가 계산한 것만 이 경로로 저장되고, 클라이언트는 결과를 표시만 한다.
    public class KillRewardPersister
    {
        private const string InternalApiKeyHeader = "X-Internal-Api-Key";

        private readonly HttpClient _httpClient;
        private readonly string _internalApiKey;

        public KillRewardPersister(IConfiguration configuration)
        {
            string mainServerBaseUrl = configuration["AuthServer:BaseUrl"]
                ?? throw new InvalidOperationException("AuthServer:BaseUrl 설정이 없습니다.");
            _internalApiKey = configuration["InternalApi:Key"] is { Length: > 0 } key
                ? key
                : throw new InvalidOperationException("InternalApi:Key 설정이 없습니다.");

            var handler = new HttpClientHandler();
#if DEBUG
            // PlayerAuthValidator와 동일한 이유 - 로컬 개발 MainServer의 자체 서명 인증서를 신뢰하기 위한 우회(Release 제외).
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
#endif
            // 처치 요청을 처리하는 세션이 저장 완료를 기다리므로, MainServer가 응답하지 않을 때 기본값(100초)만큼
            // 그 세션의 패킷 처리가 멈추지 않도록 짧게 제한한다.
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(mainServerBaseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        // level/exp는 GameServer가 계산한 최종 값, goldGained/items는 이번 처치로 더할 증가분이다.
        // 실패해도 예외를 던지지 않고 false만 반환한다 - 저장 실패로 게임 세션 자체가 끊기면 안 되기 때문이다.
        public async Task<bool> SaveAsync(long characterId, int level, int exp, int goldGained, IReadOnlyList<(string ItemId, int Qty)> items, CancellationToken ct)
        {
            var body = new
            {
                _level = level,
                _exp = exp,
                _goldGained = (long)goldGained,
                _items = items.Select(item => new { _itemId = item.ItemId, _qty = item.Qty }).ToList()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/characters/{characterId}/kill-rewards")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add(InternalApiKeyHeader, _internalApiKey);

            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[GameServer] 처치 보상 저장 실패 (CharacterId={characterId}) : HTTP {(int)response.StatusCode}");
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // MainServer 연결 실패/타임아웃(HttpClient 타임아웃은 TaskCanceledException으로 온다) - 서버 종료로 인한
                // 취소만 호출측으로 흘려보내고, 나머지는 로그만 남긴다.
                Console.WriteLine($"[GameServer] 처치 보상 저장 실패 (CharacterId={characterId}) : {ex.Message}");
                return false;
            }
        }
    }
}
