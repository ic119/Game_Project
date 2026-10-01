using GameServer.Logging;
using Microsoft.Extensions.Logging;
namespace GameServer.Networking
{
    // 몬스터 처치 보상을 MainServer에 저장한다. 세션과 분리된 백그라운드 작업으로, 실패하면 잠시 뒤 다시 보낸다.
    // 예전에는 처치한 세션의 수신 루프가 저장을 직접 기다렸다(ClientSession.HandleMonsterAttackRequestAsync) -
    // 한 번 실패하면(MainServer 순단/타임아웃) 골드/아이템이 로그만 남기고 사라졌고, 세션 종료 토큰으로 호출해 처치 직후
    // 접속을 끊으면 저장이 중간에 취소됐으며, MainServer가 느린 동안 그 플레이어의 이동/공격 처리까지 멈췄다.
    //
    // - 순서: level/exp는 최종 값이라 늦게 처리된 이전 보상이 새 값을 덮어쓰면 안 된다. 캐릭터마다 이전 저장이 끝난 뒤에
    //   다음 저장을 시작한다(세션이 바뀌어도 같은 캐릭터면 같은 순서를 따른다).
    // - 중복: 재시도한 요청이 실제로는 앞서 반영된 뒤일 수 있다(응답만 못 받은 타임아웃). 보상마다 id를 붙여 MainServer가
    //   같은 보상을 한 번만 반영한다(KillRewardReceipt).
    // - 재접속: 저장이 남은 캐릭터가 다시 입장하면 DB에서 읽은 level/exp가 옛 값이다. 입장 처리가 WaitForPendingAsync로
    //   남은 저장을 먼저 기다린다.
    public class KillRewardSaver
    {
        private static readonly ILogger Log = GameLog.For<KillRewardSaver>();

        // 재시도 간격. 합계 약 4분 반 동안 다시 보낸다 - MainServer 재시작 정도는 이 안에 끝난다.
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)
        };

        private readonly MainServerInternalApi _mainServerApi;

        // 서버 수명 토큰(Ctrl+C)이 아니라 별도 토큰이다 - 서버가 종료 신호를 받은 뒤에도 DrainAsync가 남은 저장을 마저
        // 보내야 하기 때문이다. DrainAsync가 제한 시간이 지나면 취소한다.
        private readonly CancellationTokenSource _stopCts = new();

        // 캐릭터 id -> 그 캐릭터의 마지막 저장 작업. 새 저장은 이 작업이 끝난 뒤 시작하고, 자신이 마지막이 된다.
        private readonly object _lock = new();
        private readonly Dictionary<long, Task> _tails = new();

        public KillRewardSaver(MainServerInternalApi mainServerApi)
        {
            _mainServerApi = mainServerApi;
        }

        // 저장을 예약하고 바로 돌아온다. level/exp는 이 처치까지 반영한 최종 값, gold/items는 이번 처치의 증가분이다.
        public void Enqueue(long characterId, int level, int exp, int goldGained, IReadOnlyList<(string ItemId, int Qty)> items)
        {
            var reward = new PendingKillReward(Guid.NewGuid(), characterId, level, exp, goldGained, items);

            lock (_lock)
            {
                Task previous = _tails.TryGetValue(characterId, out Task? tail) ? tail : Task.CompletedTask;
                Task next = SaveAfterAsync(previous, reward);
                _tails[characterId] = next;

                // 끝났을 때 자신이 여전히 마지막이면 목록에서 뺀다(그 사이 새 저장이 뒤에 붙었으면 그대로 둔다).
                _ = next.ContinueWith(_ => RemoveTailIfLast(characterId, next), TaskScheduler.Default);
            }
        }

        // 이 캐릭터의 남은 저장이 모두 끝날 때까지 최대 timeout만큼 기다린다. 끝났으면(또는 남은 게 없으면) true.
        public async Task<bool> WaitForPendingAsync(long characterId, TimeSpan timeout, CancellationToken ct)
        {
            Task? tail;
            lock (_lock)
            {
                _tails.TryGetValue(characterId, out tail);
            }

            if (tail is null)
            {
                return true;
            }

            await Task.WhenAny(tail, Task.Delay(timeout, ct));
            ct.ThrowIfCancellationRequested();
            return tail.IsCompleted;
        }

        // 서버 종료 시 호출한다. 남은 저장을 최대 timeout만큼 기다린 뒤, 그래도 남은 것은 취소한다(취소된 보상은 로그로 남는다).
        public async Task DrainAsync(TimeSpan timeout)
        {
            Task[] pending;
            lock (_lock)
            {
                pending = _tails.Values.ToArray();
            }

            if (pending.Length > 0)
            {
                Log.LogInformation("종료 전 처치 보상 저장 대기 중 ({Count}명)...", pending.Length);
                await Task.WhenAny(Task.WhenAll(pending), Task.Delay(timeout));
            }

            _stopCts.Cancel();
            await Task.WhenAll(pending);
        }

        private void RemoveTailIfLast(long characterId, Task task)
        {
            lock (_lock)
            {
                if (_tails.TryGetValue(characterId, out Task? tail) && ReferenceEquals(tail, task))
                {
                    _tails.Remove(characterId);
                }
            }
        }

        // 예외를 밖으로 내보내지 않는다 - 뒤에 붙은 저장이 이 작업을 기다리므로, 여기서 실패해도 다음 저장은 진행돼야 한다.
        private async Task SaveAfterAsync(Task previous, PendingKillReward reward)
        {
            // Enqueue의 lock 안에서 HTTP 호출까지 동기로 진행되지 않도록 먼저 양보한다.
            await Task.Yield();
            await previous;

            CancellationToken ct = _stopCts.Token;
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    InternalApiResult result = await _mainServerApi.SaveKillRewardsAsync(
                        reward.RewardId, reward.CharacterId, reward.Level, reward.Exp, reward.GoldGained, reward.Items, ct);

                    if (result == InternalApiResult.Success)
                    {
                        if (attempt > 0)
                        {
                            Log.LogInformation("처치 보상 저장 재시도 성공 ({Attempt}회 재시도) : {Reward}", attempt, reward);
                        }
                        return;
                    }

                    if (result == InternalApiResult.PermanentFailure)
                    {
                        Log.LogWarning("처치 보상 저장 거부 - 보상이 반영되지 않았습니다 : {Reward}", reward);
                        return;
                    }

                    if (attempt >= RetryDelays.Length)
                    {
                        Log.LogError("처치 보상 저장 재시도 한도 초과 - 보상이 반영되지 않았습니다 : {Reward}", reward);
                        return;
                    }

                    await Task.Delay(RetryDelays[attempt], ct);
                }
            }
            catch (OperationCanceledException)
            {
                Log.LogWarning("서버 종료로 처치 보상 저장 취소 - 보상이 반영되지 않았습니다 : {Reward}", reward);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "처치 보상 저장 중 오류 - 보상이 반영되지 않았습니다 : {Reward}", reward);
            }
        }

        // 저장에 끝내 실패하면 이 내용 그대로 로그에 남긴다(수동 복구용).
        private sealed record PendingKillReward(Guid RewardId, long CharacterId, int Level, int Exp, int GoldGained, IReadOnlyList<(string ItemId, int Qty)> Items)
        {
            public override string ToString() =>
                $"RewardId={RewardId}, CharacterId={CharacterId}, Level={Level}, Exp={Exp}, Gold=+{GoldGained}, Items=[{string.Join(", ", Items.Select(item => $"{item.ItemId}x{item.Qty}"))}]";
        }
    }
}
