using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using GameServer.Combat;
using GameServer.Items;
using GameServer.Maps;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 스탯 재조회, 아이템(물약) 사용, 상자 개봉.
    public partial class ClientSession
    {
        // 스탯 재조회(MainServer 호출) 최소 간격. 장비를 연달아 바꾸면 요청이 몰리는데, 그만큼 MainServer를 호출하지 않고
        // 하나로 합친다(아래 RunStatRefreshLoopAsync).
        private static readonly TimeSpan MinStatRefreshInterval = TimeSpan.FromSeconds(1);

        // 재조회 루프가 돌고 있으면 1. 루프 밖(수신 루프)과 루프 안(백그라운드)에서 함께 읽고 쓰므로 Interlocked로 다룬다.
        private int _statRefreshRunning;

        // 마지막 재조회 이후 새 요청이 들어왔으면 1.
        private int _statRefreshPending;

        // 인벤토리에서 장비를 장착/해제해 공격력/방어력이 바뀌었을 때 클라이언트가 보낸다. request.AttackPower/
        // Defense(클라이언트 자기 계산값)는 신뢰하지 않고 트리거로만 쓴다 - Game_EnterRequest 때와 동일하게
        // MainServer에서 str/agi/장착 아이템을 다시 조회해 서버가 직접 재계산한다(CombatStatCalculator).
        // 브로드캐스트는 필요 없어(GameRoom.TryUpdateCombatStats 주석 참고) 응답 없이 서버 캐시만 갱신한다.
        // 요청마다 바로 조회하지 않고 "갱신 필요" 표시만 한 뒤, 재조회 루프가 최소 간격을 지키며 한 번에 처리한다 - 요청을 그냥
        // 버리면 마지막 장비 상태가 반영되지 않을 수 있어서, 버리는 대신 합친다(마지막 상태는 항상 반영된다).
        private void HandleStatUpdateRequest(byte[] body, CancellationToken ct)
        {
            var request = C2SStatUpdateRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId)
            {
                return;
            }

            Volatile.Write(ref _statRefreshPending, 1);
            if (Interlocked.CompareExchange(ref _statRefreshRunning, 1, 0) == 0)
            {
                _ = RunStatRefreshLoopAsync(playerId, ct);
            }
        }

        // 수신 루프를 막지 않도록 백그라운드에서 돈다. 사용자 AccessToken(30분 만료)이 아니라 서버 간 API로 조회하므로
        // 오래 접속해 있어도 장비 변경이 계속 반영된다.
        private async Task RunStatRefreshLoopAsync(long playerId, CancellationToken ct)
        {
            try
            {
                while (Interlocked.Exchange(ref _statRefreshPending, 0) == 1)
                {
                    CharacterSnapshot? snapshot = await _mainServerApi.FetchCharacterAsync(playerId, ct);
                    if (snapshot is null)
                    {
                        // MainServer 순단 등으로 조회에 실패한 경우 - 이전에 검증된 값을 그대로 유지하고 이번 갱신만 건너뛴다.
                        Console.WriteLine($"[GameServer] Game_StatUpdateRequest 스탯 재조회 실패 (PlayerId={playerId}) - 이전 값을 유지합니다.");
                    }
                    else if (_room is { } room)
                    {
                        // 공격력/방어력은 서버 메모리의 레벨(접속 중 오른 레벨)로 다시 계산한다(PlayerCombatStats.ApplySnapshot).
                        // DB의 snapshot.Level은 킬 보상 저장이 늦으면 아직 이전 레벨일 수 있어 쓰지 않는다.
                        room.TryUpdateCombatStats(playerId, snapshot);
                        room.TryUpdateEquipment(playerId, EquippedVisuals.From(snapshot, ItemCatalog.Exists));
                    }

                    await Task.Delay(MinStatRefreshInterval, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 세션 종료.
            }
            finally
            {
                Volatile.Write(ref _statRefreshRunning, 0);

                // 루프가 "대기 요청 없음"을 확인한 직후 새 요청이 들어오면, 그 요청은 루프가 돌고 있다고 보고 새로 시작하지 않았다 -
                // 여기서 한 번 더 확인해 놓치지 않게 한다.
                if (!ct.IsCancellationRequested && Volatile.Read(ref _statRefreshPending) == 1
                    && Interlocked.CompareExchange(ref _statRefreshRunning, 1, 0) == 0)
                {
                    _ = RunStatRefreshLoopAsync(playerId, ct);
                }
            }
        }

        // 물약 연타로 MainServer 차감 요청이 몰리지 않게 하는 최소 사용 간격.
        private const double MinUseItemIntervalMs = 300;
        private DateTime _lastUseItemAtUtc = DateTime.MinValue;

        // 물약 재사용 대기시간(모든 물약이 공유). 위 최소 간격은 요청 폭주(MainServer 왕복)를 막는 용도이고, 이쪽은
        // 회복 속도 상한(ItemDefinition.UseCooldownSeconds)을 강제한다. 세션마다 하나라 재접속하면 초기화된다.
        private readonly PotionCooldown _potionCooldown = new();

        // 소비 아이템(현재는 회복 물약) 사용. 예전에는 클라이언트가 로컬에서 회복하고 MainServer에 차감만 따로 요청해
        // 서버 HP에는 회복이 반영되지 않았다. 이제 서버가 효과 여부를 확인 -> MainServer에서 1개 차감 -> 회복 순서로
        // 처리하고, 결과를 요청자에게(Game_UseItemResult), 바뀐 체력을 방 전체에(Game_PlayerHpBroadcast) 알린다.
        private async Task HandleUseItemRequestAsync(byte[] body, CancellationToken ct)
        {
            var request = C2SUseItemRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

            UseItemFailReason failReason = await TryUseItemAsync(playerId, room, request.ItemId, ct);

            // 남은 대기시간은 성공/실패와 무관하게 늘 담는다 - 클라이언트가 이 값으로 사용 버튼을 잠그고 카운트다운한다.
            // (성공하면 방금 시작된 대기시간, 대기 중 거부되면 남은 시간, 아니면 0)
            var result = new S2CUseItemResult
            {
                ItemId = request.ItemId,
                Success = failReason == UseItemFailReason.None,
                FailReason = failReason,
                CooldownRemainingMs = (int)Math.Min(int.MaxValue, _potionCooldown.RemainingMs())
            };
            Send(OpCode.Game_UseItemResult, result.Encode());
        }

        // 보물상자 개봉. GameRoom.TryOpenChest가 사거리/선착순을 원자적으로 판정하므로 여기서는 결과만 보고한다 -
        // 실패(이미 열렸음/사거리 밖/존재하지 않는 ChestId)는 조용히 무시한다(위조 시도와 정상적인 경쟁 실패를
        // 구분할 필요가 없다).
        private void HandleChestOpenRequest(byte[] body)
        {
            var request = C2SChestOpenRequest.Decode(body);

            if (_playerId is not { } playerId || _room is not { } room)
            {
                return;
            }

            if (!room.TryOpenChest(request.ChestId, playerId, out int gold, out List<(string ItemId, int Qty)> items))
            {
                return;
            }

            // 골드/아이템은 개봉한 본인에게만(Game_LootBroadcast 재사용 - MonsterId는 몬스터 전용 필드라 여기선 0).
            // 빈 손으로 열렸으면(운 나쁘게 아무것도 안 나옴) 보낼 것도 저장할 것도 없다.
            if ((gold > 0 || items.Count > 0) && room.TryGetInfo(playerId, out PlayerInfo? player))
            {
                var loot = new S2CLootBroadcast { MonsterId = 0, GoldGained = gold, Items = items };
                Send(OpCode.Game_LootBroadcast, loot.Encode());

                // KillRewardSaver는 이름은 "처치" 보상이지만 실제로는 "캐릭터의 최종 level/exp + 이번 골드/아이템 증가분"을
                // 저장하는 범용 경로라 상자 보상도 그대로 재사용한다(같은 중복 방지/재시도 보장이 필요하기 때문).
                _killRewardSaver.Enqueue(playerId, player.Level, player.Exp, gold, items);
            }

            // 뚜껑이 열렸다는 사실은 골드/아이템 내용과 무관하게 방 전체에 알린다(빈 상자여도 시각적으로는 열려야 한다).
            var opened = new S2CChestOpenBroadcast { ChestId = request.ChestId };
            room.BroadcastToAll(OpCode.Game_ChestOpenBroadcast, opened.Encode());
        }

        // 성공이면 None, 아니면 거부 사유(클라이언트가 안내에 쓴다 - Game_UseItemResult.FailReason).
        private async Task<UseItemFailReason> TryUseItemAsync(long playerId, GameRoom room, string itemId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUseItemAtUtc).TotalMilliseconds < MinUseItemIntervalMs)
            {
                return UseItemFailReason.TooFast;
            }
            _lastUseItemAtUtc = now;

            // 회복 아이템이 아니거나, 써도 효과가 없는 상태(사망/만피)면 차감하지 않는다.
            if (!ItemCatalog.TryGet(itemId, out ItemDefinition definition) || definition.HealPercent <= 0
                || !room.CanBeHealed(playerId))
            {
                return UseItemFailReason.NotUsable;
            }

            // 재사용 대기시간을 소모 전에 먼저 건다(예약) - MainServer 왕복(await) 중에 들어오는 같은 세션의 다음 요청도 막힌다.
            // 대기 중이면 아이템을 소모하지 않고 거부한다.
            if (!_potionCooldown.TryReserve(definition.UseCooldownSeconds, out PotionCooldown.Reservation reservation, out _))
            {
                return UseItemFailReason.Cooldown;
            }

            if (!await _mainServerApi.ConsumeItemAsync(playerId, itemId, ct))
            {
                // 아이템이 소모되지 않았으니 이번 시도가 대기시간을 잡아먹지 않게 되돌린다(미보유/저장 실패 등).
                _potionCooldown.Cancel(reservation);
                return UseItemFailReason.Rejected;
            }

            // 차감과 회복 사이에 사망/만피가 되면 아이템만 소모된다 - 두 호출 사이(MainServer 왕복 동안)의 짧은 틈이라
            // 드물고, 반대로 회복 먼저 하면 차감 실패 시 공짜 회복이 되므로 차감을 먼저 한다.
            return room.TryHealPlayer(playerId, definition.HealPercent) ? UseItemFailReason.None : UseItemFailReason.Rejected;
        }
    }
}
