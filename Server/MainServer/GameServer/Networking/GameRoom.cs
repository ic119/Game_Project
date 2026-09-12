using System.Collections.Concurrent;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // AOI/룸 시스템이 없는 동안, 접속한 전원이 속하는 단일 글로벌 그룹.
    // 접속자 레지스트리 + "본인 제외 전원 브로드캐스트" 헬퍼를 제공한다.
    public class GameRoom
    {
        private readonly ConcurrentDictionary<long, (PlayerInfo Info, ClientSession Session)> _players = new();

        public void Add(PlayerInfo info, ClientSession session)
        {
            _players[info.PlayerId] = (info, session);
        }

        public void UpdatePosition(long playerId, float x, float y, float z, float rotationY)
        {
            if (_players.TryGetValue(playerId, out var entry))
            {
                entry.Info.X = x;
                entry.Info.Y = y;
                entry.Info.Z = z;
                entry.Info.RotationY = rotationY;
            }
        }

        public void Remove(long playerId)
        {
            _players.TryRemove(playerId, out _);
        }

        // playerId를 제외한 현재 접속자 스냅샷(신규 입장자에게 Game_EnterAck으로 보내줄 목록).
        public List<PlayerInfo> SnapshotExcluding(long playerId)
        {
            var result = new List<PlayerInfo>();
            foreach (var (info, _) in _players.Values)
            {
                if (info.PlayerId != playerId)
                {
                    result.Add(info);
                }
            }
            return result;
        }

        public async Task BroadcastAsync(OpCode opCode, byte[] body, long excludePlayerId, CancellationToken ct)
        {
            foreach (var (info, session) in _players.Values)
            {
                if (info.PlayerId == excludePlayerId)
                {
                    continue;
                }

                await session.SendAsync(opCode, body, ct);
            }
        }
    }
}
