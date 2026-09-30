using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Maps
{
    // 방(GameRoom) 하나의 상자 상태: 지금 서 있는 상자, 이미 열린 상자, 그리고 열린 뒤의 제거/리스폰 타이머.
    // GameRoom에서 분리한 이유는 브로드캐스트와 지연(delay)을 주입받아 GameRoom(틱 루프/접속자 등) 없이 테스트하기 위해서다.
    //
    // 동시성: 개봉 요청(세션 스레드)과 제거/리스폰 타이머(스레드 풀)가 같은 집합을 바꾸므로 모든 접근을 _lock으로 감싼다.
    // 브로드캐스트/전송은 각 세션의 전송 대기열에 넣기만 하고 바로 돌아오므로(GameRoom.BroadcastToAll 주석 참고) 잠금 안에서 해도 된다 -
    // 그래야 "스냅샷을 만들어 보낸 뒤 그 이후의 변경 브로드캐스트가 뒤따른다"는 순서가 보장돼, 방금 입장한 클라이언트가 놓친 변화 없이 따라잡는다.
    //
    // 리스폰은 후보(ChestCandidates)에서 나온 상자에만 적용된다(고정 상자는 한 번 열리면 그대로). 열린 상자는 DespawnDelaySeconds 뒤에
    // 제거되고, RespawnSeconds가 되면 같은 LootTableKey의 다른 빈 후보에 새 상자가 생긴다(등급별 개수가 유지된다).
    public class RoomChestState
    {
        private readonly object _lock = new();
        private readonly List<MapChest> _active = new();
        private readonly HashSet<string> _openedIds = new();
        private readonly List<MapChest> _candidates;
        private readonly HashSet<string> _candidateIds;
        private readonly Dictionary<string, MapChestSpawnCount> _countsByKey;
        private readonly Random _random;
        private readonly Action<OpCode, byte[]> _broadcast;
        private readonly CancellationToken _ct;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public RoomChestState(MapData? mapData, Random random, Action<OpCode, byte[]> broadcast, CancellationToken ct, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _random = random;
            _broadcast = broadcast;
            _ct = ct;
            _delay = delay ?? Task.Delay;

            if (mapData is null)
            {
                _candidates = new List<MapChest>();
                _candidateIds = new HashSet<string>();
                _countsByKey = new Dictionary<string, MapChestSpawnCount>();
                return;
            }

            _candidates = mapData.ChestCandidates;
            _candidateIds = _candidates.Select(c => c.Id).ToHashSet();
            _countsByKey = mapData.ChestSpawnCounts.ToDictionary(c => c.LootTableKey);

            _active.AddRange(mapData.Chests);
            _active.AddRange(ChestSpawnSelector.Select(_candidates, mapData.ChestSpawnCounts, random));
        }

        public IReadOnlyList<MapChest> GetActiveSnapshot()
        {
            lock (_lock)
            {
                return _active.ToList();
            }
        }

        // 상자를 연다. 존재하지 않는 상자/사거리 밖/이미 열린 상자면 실패(false) - 선착순은 이 잠금 안의 판정이 원자적으로 보장한다.
        // 성공하면 후보 상자에 한해 제거/리스폰 타이머를 시작한다. 드롭 굴리기와 브로드캐스트는 호출측(GameRoom/ClientSession)이 한다.
        public bool TryOpen(string chestId, float x, float z, out MapChest? chest)
        {
            MapChestSpawnCount? respawn = null;

            lock (_lock)
            {
                chest = _active.Find(c => c.Id == chestId);
                if (chest is null || !chest.IsWithinRange(x, z) || !_openedIds.Add(chestId))
                {
                    chest = null;
                    return false;
                }

                if (_candidateIds.Contains(chestId)
                    && _countsByKey.TryGetValue(chest.LootTableKey, out MapChestSpawnCount? config)
                    && config.RespawnSeconds > 0f)
                {
                    respawn = config;
                }
            }

            if (respawn is not null)
            {
                _ = RunRespawnAsync(chest!, respawn);
            }

            return true;
        }

        // 방에 새로 입장/맵 이동한 세션에게 지금 상태를 알린다: 서 있는 상자 목록이 먼저, 그중 이미 열린 것이 그다음.
        // 연결이 순서를 보장하므로 클라이언트는 열림 알림을 받을 때 대상 상자를 이미 알고 있다.
        public void SendState(Action<OpCode, byte[]> send)
        {
            lock (_lock)
            {
                var packet = new S2CActiveChests { Chests = _active.Select(ToInfo).ToList() };
                send(OpCode.Game_ActiveChestsNotify, packet.Encode());

                foreach (string chestId in _openedIds)
                {
                    send(OpCode.Game_ChestOpenBroadcast, new S2CChestOpenBroadcast { ChestId = chestId }.Encode());
                }
            }
        }

        private async Task RunRespawnAsync(MapChest opened, MapChestSpawnCount config)
        {
            try
            {
                await _delay(TimeSpan.FromSeconds(config.DespawnDelaySeconds), _ct);
                Despawn(opened.Id);

                await _delay(TimeSpan.FromSeconds(config.RespawnSeconds - config.DespawnDelaySeconds), _ct);
                SpawnReplacement(opened);
            }
            catch (OperationCanceledException)
            {
                // 서버 종료로 인한 정상 취소 - 리스폰하지 않는다.
            }
        }

        private void Despawn(string chestId)
        {
            lock (_lock)
            {
                if (_active.RemoveAll(c => c.Id == chestId) == 0)
                {
                    return;
                }

                // 같은 id의 상자가 나중에 다시 서면 닫힌 상태여야 한다.
                _openedIds.Remove(chestId);
                _broadcast(OpCode.Game_ChestDespawnBroadcast, new S2CChestDespawnBroadcast { ChestId = chestId }.Encode());
            }
        }

        private void SpawnReplacement(MapChest opened)
        {
            lock (_lock)
            {
                var activeIds = _active.Select(c => c.Id).ToHashSet();
                MapChest? next = ChestSpawnSelector.PickReplacement(_candidates, activeIds, opened.LootTableKey, opened.Id, _random);
                if (next is null)
                {
                    return;
                }

                _active.Add(next);
                _broadcast(OpCode.Game_ChestSpawnBroadcast, new S2CChestSpawnBroadcast { Chest = ToInfo(next) }.Encode());
            }
        }

        private static ActiveChestInfo ToInfo(MapChest chest) => new()
        {
            Id = chest.Id,
            X = chest.X,
            Y = chest.Y,
            Z = chest.Z,
            LootTableKey = chest.LootTableKey
        };
    }
}
