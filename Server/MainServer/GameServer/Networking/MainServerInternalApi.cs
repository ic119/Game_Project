using System.Net.Http.Json;
using GameServer.Combat;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    // MainServer의 서버 간 API(api/internal/characters/...)를 호출해 캐릭터 데이터를 직접 바꾼다.
    // 몬스터 처치 보상 저장과 소비 아이템 차감처럼 "클라이언트가 값이나 시점을 정하면 안 되는" 쓰기 작업이 대상이다 -
    // 예전에는 클라이언트가 MainServer에 대신 저장을 요청해 보상 값을 위조하거나, 아이템 차감과 효과(회복)를
    // 따로 처리할 수 있었다. 이제 GameServer가 결과를 계산/적용하면서 이 경로로만 저장하고, 클라이언트는 표시만 한다.
    public class MainServerInternalApi
    {
        private const string InternalApiKeyHeader = "X-Internal-Api-Key";

        private readonly HttpClient _httpClient;
        private readonly string _internalApiKey;

        public MainServerInternalApi(IConfiguration configuration)
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
            // 호출한 세션이 응답을 기다리므로, MainServer가 응답하지 않을 때 기본값(100초)만큼
            // 그 세션의 패킷 처리가 멈추지 않도록 짧게 제한한다.
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(mainServerBaseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        // 몬스터 처치 보상 저장. level/exp는 GameServer가 계산한 최종 값, goldGained/items는 이번 처치로 더할 증가분이다.
        public Task<bool> SaveKillRewardsAsync(long characterId, int level, int exp, int goldGained, IReadOnlyList<(string ItemId, int Qty)> items, CancellationToken ct)
        {
            var body = new
            {
                _level = level,
                _exp = exp,
                _goldGained = (long)goldGained,
                _items = items.Select(item => new { _itemId = item.ItemId, _qty = item.Qty }).ToList()
            };

            return PostAsync($"/api/internal/characters/{characterId}/kill-rewards", JsonContent.Create(body), "처치 보상 저장", characterId, ct);
        }

        // 소비 아이템 1개 차감. 보유하지 않은 아이템이면(400) false - 호출측은 효과를 적용하지 않는다.
        public Task<bool> ConsumeItemAsync(long characterId, string itemId, CancellationToken ct)
        {
            return PostAsync($"/api/internal/characters/{characterId}/items/{Uri.EscapeDataString(itemId)}/consume", null, "아이템 차감", characterId, ct);
        }

        // 캐릭터 원본 데이터 조회(장비 변경 후 전투 스탯 재계산용). 사용자 AccessToken은 30분이면 만료되므로 입장 이후의
        // 재조회는 이 서버 간 경로로 한다. 실패하면 null(호출측은 이전 값을 유지한다).
        public async Task<CharacterSnapshot?> FetchCharacterAsync(long characterId, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/characters/{characterId}");
            request.Headers.Add(InternalApiKeyHeader, _internalApiKey);

            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[GameServer] 캐릭터 조회 실패 (CharacterId={characterId}) : HTTP {(int)response.StatusCode}");
                    return null;
                }

                return CharacterSnapshot.FromCharacterResponseJson(await response.Content.ReadAsStringAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Console.WriteLine($"[GameServer] 캐릭터 조회 실패 (CharacterId={characterId}) : {ex.Message}");
                return null;
            }
        }

        // 실패해도 예외를 던지지 않고 false만 반환한다 - MainServer 호출 실패로 게임 세션 자체가 끊기면 안 되기 때문이다.
        private async Task<bool> PostAsync(string path, HttpContent? content, string actionName, long characterId, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            request.Headers.Add(InternalApiKeyHeader, _internalApiKey);

            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[GameServer] {actionName} 실패 (CharacterId={characterId}) : HTTP {(int)response.StatusCode}");
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // MainServer 연결 실패/타임아웃(HttpClient 타임아웃은 TaskCanceledException으로 온다) - 서버 종료로 인한
                // 취소만 호출측으로 흘려보내고, 나머지는 로그만 남긴다.
                Console.WriteLine($"[GameServer] {actionName} 실패 (CharacterId={characterId}) : {ex.Message}");
                return false;
            }
        }
    }
}
