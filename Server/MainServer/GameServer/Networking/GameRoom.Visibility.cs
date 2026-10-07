using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 관심 영역(AOI): 플레이어마다 "지금 보고 있는 플레이어/몬스터 목록(시야)"을 서버가 들고, 시야 안의 것만 보낸다.
    // 예전에는 맵 전체에 모든 이동/전투 알림을 보내 멀리 있어 보이지도 않는 대상의 알림까지 받았다.
    //
    // - 시야 갱신은 방 틱마다(UpdateViewsAndSendSnapshots). 격자(CellSize)로 후보를 추리고, 거리로 판정한다.
    //   ViewEnterRadius 안으로 들어와야 보이기 시작하고, ViewLeaveRadius 밖으로 나가야 사라진다 - 경계에서 생겼다 사라졌다를
    //   반복하지 않게 들어오는 거리와 나가는 거리를 다르게 둔다.
    // - 시야에 들어오면 전체 정보를 보낸다(플레이어 Game_PlayerJoined, 몬스터 Game_MonsterSpawnBroadcast - 기존 패킷 재사용).
    //   시야에서 나가면 Game_PlayerLeft / Game_MonsterLeaveView(사망 연출 없이 제거)를 보낸다.
    // - 스냅샷(Game_WorldSnapshot)은 받는 사람의 시야 안에서 움직인 것만 담는다(이번 틱에 새로 보인 것은 전체 정보로 이미 받으므로 제외).
    // - 전투/HP/부활 알림은 그 대상을 보고 있는 사람(+ 필요한 당사자)에게만 보낸다(SendToViewersOf...). 채팅은 맵 전체(BroadcastToAll).
    //
    // 시야 목록은 방 틱(한 스레드)과 여러 세션(입장/퇴장/이벤트 전송)이 함께 쓰므로 _viewLock으로 보호한다.
    public partial class GameRoom
    {
        private const float CellSize = 50f;
        private const float ViewEnterRadius = 60f;
        private const float ViewLeaveRadius = 75f;

        // 후보 검색 범위(칸). ViewLeaveRadius를 덮을 만큼(ceil(75/50) = 2) 주변 5x5칸을 본다.
        private const int CandidateCellRange = 2;

        private sealed class PlayerView
        {
            public readonly HashSet<long> Players = new();
            public readonly HashSet<long> Monsters = new();
        }

        private readonly object _viewLock = new();

        // 플레이어 id -> 그 플레이어의 시야. _players와 같은 시점에 추가/제거된다(Join/Remove).
        private readonly Dictionary<long, PlayerView> _views = new();

        // 방에 입장시키고, 지금 시야 안에 있는 플레이어/몬스터를 돌려준다(Game_EnterAck/Game_MapChangeAck에 담을 목록).
        // 다른 플레이어들은 다음 틱의 시야 갱신에서 이 플레이어를 "시야 진입"으로 받는다.
        public (List<PlayerInfo> VisiblePlayers, List<MonsterInfo> VisibleMonsters) Join(PlayerInfo info, ISessionSender session)
        {
            var visiblePlayers = new List<PlayerInfo>();
            var visibleMonsters = new List<MonsterInfo>();

            lock (_viewLock)
            {
                _players[info.PlayerId] = (info, session);

                var view = new PlayerView();
                foreach (var (other, _) in _players.Values)
                {
                    if (other.PlayerId != info.PlayerId && IsWithin(info, other.X, other.Z, ViewEnterRadius))
                    {
                        view.Players.Add(other.PlayerId);
                        visiblePlayers.Add(other);
                    }
                }

                foreach (MonsterRuntime runtime in _monsters.Values)
                {
                    if (runtime.Info.CurrentHp > 0 && IsWithin(info, runtime.Info.X, runtime.Info.Z, ViewEnterRadius))
                    {
                        view.Monsters.Add(runtime.Info.MonsterId);
                        visibleMonsters.Add(runtime.Info);
                    }
                }

                _views[info.PlayerId] = view;
            }

            return (visiblePlayers, visibleMonsters);
        }

        // session이 등록한 항목일 때만 제거하고, 실제로 제거했으면 true. 같은 캐릭터가 새 세션으로 다시 입장해 항목이
        // 교체된 뒤에는 이전 세션이 종료되면서 호출해도 새 세션의 항목을 지우지 않는다(퇴장 알림도 보내지 않는다).
        // 제거하면 이 플레이어를 보고 있던 사람들에게만 Game_PlayerLeft를 보낸다.
        public bool Remove(long playerId, ISessionSender session)
        {
            lock (_viewLock)
            {
                if (!_players.TryGetValue(playerId, out var entry) || !ReferenceEquals(entry.Session, session)
                    || !_players.TryRemove(new KeyValuePair<long, (PlayerInfo Info, ISessionSender Session)>(playerId, entry)))
                {
                    return false;
                }

                _views.Remove(playerId);
                _movedPlayerIds.TryRemove(playerId, out _);
                _dashStates.TryRemove(playerId, out _);
                _skillStates.TryRemove(playerId, out _);

                byte[] left = new S2CPlayerLeft { PlayerId = playerId }.Encode();
                foreach (var (viewerId, view) in _views)
                {
                    if (view.Players.Remove(playerId) && _players.TryGetValue(viewerId, out var viewer))
                    {
                        viewer.Session.Send(OpCode.Game_PlayerLeft, left);
                    }
                }

                return true;
            }
        }

        // playerId를 보고 있는 사람과 본인(+ alsoToPlayerId)에게 보낸다. 피격/HP/부활처럼 본인 화면에도 반영돼야 하는 알림용.
        // excludeSelf가 true이면 본인(playerId)은 제외한다 - 본인은 이미 로컬에서 처리한 연출을 다른 사람에게만 중계할 때 쓴다.
        private void SendToViewersOfPlayer(long playerId, OpCode opCode, byte[] body, long? alsoToPlayerId = null, bool excludeSelf = false)
        {
            lock (_viewLock)
            {
                foreach (var (viewerId, view) in _views)
                {
                    if (excludeSelf && viewerId == playerId)
                    {
                        continue;
                    }

                    if ((viewerId == playerId || viewerId == alsoToPlayerId || view.Players.Contains(playerId))
                        && _players.TryGetValue(viewerId, out var viewer))
                    {
                        viewer.Session.Send(opCode, body);
                    }
                }
            }
        }

        // monsterId를 보고 있는 사람(+ alsoToPlayerId: 공격자/피격자 같은 당사자)에게 보낸다.
        private void SendToViewersOfMonster(long monsterId, OpCode opCode, byte[] body, long? alsoToPlayerId = null)
        {
            lock (_viewLock)
            {
                foreach (var (viewerId, view) in _views)
                {
                    if ((viewerId == alsoToPlayerId || view.Monsters.Contains(monsterId))
                        && _players.TryGetValue(viewerId, out var viewer))
                    {
                        viewer.Session.Send(opCode, body);
                    }
                }
            }
        }

        // 죽은 몬스터를 모든 시야에서 지운다(사망 알림은 이미 보냈으므로 시야 이탈 알림은 보내지 않는다).
        private void ForgetMonsterInAllViews(long monsterId)
        {
            lock (_viewLock)
            {
                foreach (PlayerView view in _views.Values)
                {
                    view.Monsters.Remove(monsterId);
                }
            }
        }

        // 방 틱마다: 받는 사람별로 시야를 다시 계산해 진입/이탈을 알리고, 시야 안에서 움직인 것만 스냅샷으로 보낸다.
        private void UpdateViewsAndSendSnapshots(List<EntityTransform> movedPlayers, List<EntityTransform> movedMonsters)
        {
            long serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (_viewLock)
            {
                if (_views.Count == 0)
                {
                    return;
                }

                // 격자 칸 -> 그 칸에 있는 플레이어/몬스터. 받는 사람마다 주변 칸만 보면 되도록 한 번만 만든다.
                var playerGrid = new Dictionary<(int, int), List<PlayerInfo>>();
                foreach (var (info, _) in _players.Values)
                {
                    AddToGrid(playerGrid, info.X, info.Z, info);
                }

                var monsterGrid = new Dictionary<(int, int), List<MonsterInfo>>();
                foreach (MonsterRuntime runtime in _monsters.Values)
                {
                    if (runtime.Info.CurrentHp > 0)
                    {
                        AddToGrid(monsterGrid, runtime.Info.X, runtime.Info.Z, runtime.Info);
                    }
                }

                foreach (var (viewerId, view) in _views)
                {
                    if (!_players.TryGetValue(viewerId, out var viewerEntry))
                    {
                        continue;
                    }

                    PlayerInfo viewer = viewerEntry.Info;
                    ISessionSender session = viewerEntry.Session;

                    // 플레이어 시야
                    var nextPlayers = new HashSet<long>();
                    var enteredPlayers = new HashSet<long>();
                    foreach (PlayerInfo other in Nearby(playerGrid, viewer))
                    {
                        if (other.PlayerId == viewerId)
                        {
                            continue;
                        }

                        bool wasVisible = view.Players.Contains(other.PlayerId);
                        if (IsWithin(viewer, other.X, other.Z, wasVisible ? ViewLeaveRadius : ViewEnterRadius))
                        {
                            nextPlayers.Add(other.PlayerId);
                            if (!wasVisible)
                            {
                                enteredPlayers.Add(other.PlayerId);
                                session.Send(OpCode.Game_PlayerJoined, new S2CPlayerJoined { Player = other }.Encode());
                            }
                        }
                    }

                    foreach (long previous in view.Players)
                    {
                        if (!nextPlayers.Contains(previous))
                        {
                            session.Send(OpCode.Game_PlayerLeft, new S2CPlayerLeft { PlayerId = previous }.Encode());
                        }
                    }

                    // 몬스터 시야
                    var nextMonsters = new HashSet<long>();
                    var enteredMonsters = new HashSet<long>();
                    foreach (MonsterInfo monster in Nearby(monsterGrid, viewer))
                    {
                        bool wasVisible = view.Monsters.Contains(monster.MonsterId);
                        if (IsWithin(viewer, monster.X, monster.Z, wasVisible ? ViewLeaveRadius : ViewEnterRadius))
                        {
                            nextMonsters.Add(monster.MonsterId);
                            if (!wasVisible)
                            {
                                enteredMonsters.Add(monster.MonsterId);
                                session.Send(OpCode.Game_MonsterSpawnBroadcast, new S2CMonsterSpawnBroadcast { Monster = monster }.Encode());
                            }
                        }
                    }

                    foreach (long previous in view.Monsters)
                    {
                        if (!nextMonsters.Contains(previous))
                        {
                            session.Send(OpCode.Game_MonsterLeaveView, new S2CMonsterLeaveView { MonsterId = previous }.Encode());
                        }
                    }

                    view.Players.Clear();
                    view.Players.UnionWith(nextPlayers);
                    view.Monsters.Clear();
                    view.Monsters.UnionWith(nextMonsters);

                    // 스냅샷: 시야 안에서 움직였고 이번 틱에 새로 보인 게 아닌 것만(새로 보인 건 위 전체 정보에 위치가 들어 있다).
                    var snapshotPlayers = movedPlayers.FindAll(p => nextPlayers.Contains(p.Id) && !enteredPlayers.Contains(p.Id));
                    var snapshotMonsters = movedMonsters.FindAll(m => nextMonsters.Contains(m.Id) && !enteredMonsters.Contains(m.Id));
                    if (snapshotPlayers.Count > 0 || snapshotMonsters.Count > 0)
                    {
                        var snapshot = new S2CWorldSnapshot { ServerTimeMs = serverTimeMs, Players = snapshotPlayers, Monsters = snapshotMonsters };
                        session.Send(OpCode.Game_WorldSnapshot, snapshot.Encode());
                    }
                }
            }
        }

        private static (int, int) CellOf(float x, float z) => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize));

        private static void AddToGrid<T>(Dictionary<(int, int), List<T>> grid, float x, float z, T item)
        {
            var cell = CellOf(x, z);
            if (!grid.TryGetValue(cell, out var list))
            {
                list = new List<T>();
                grid[cell] = list;
            }
            list.Add(item);
        }

        // viewer 주변 (2*CandidateCellRange+1)^2 칸에 있는 항목들(거리 판정 전 후보).
        private static IEnumerable<T> Nearby<T>(Dictionary<(int, int), List<T>> grid, PlayerInfo viewer)
        {
            var (centerX, centerZ) = CellOf(viewer.X, viewer.Z);
            for (int dx = -CandidateCellRange; dx <= CandidateCellRange; dx++)
            {
                for (int dz = -CandidateCellRange; dz <= CandidateCellRange; dz++)
                {
                    if (grid.TryGetValue((centerX + dx, centerZ + dz), out var list))
                    {
                        foreach (T item in list)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        private static bool IsWithin(PlayerInfo viewer, float x, float z, float radius)
        {
            float dx = x - viewer.X;
            float dz = z - viewer.Z;
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
