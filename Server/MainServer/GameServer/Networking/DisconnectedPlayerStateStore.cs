using System.Collections.Concurrent;

namespace GameServer.Networking
{
    // 접속이 끊긴 플레이어의 전투 상태(체력/맵/위치)를 잠시 보관했다가, 같은 캐릭터가 다시 입장하면 돌려준다.
    // 체력은 DB에 저장하지 않고 입장할 때마다 가득 찬 상태로 시작하므로, 예전에는 전투 중 체력이 떨어지면 접속을 끊었다
    // 다시 들어오는 것만으로 바로 회복됐다(사망 직전 회피). 끊긴 뒤 RetentionTime 안에 다시 들어오면 끊길 때 체력 그대로
    // 시작하고, 그보다 오래 지나면 예전처럼 가득 찬 체력으로 시작한다(접속하지 않은 동안 회복된 것으로 본다).
    // 서버 메모리에만 두므로 GameServer가 재시작되면 사라진다(그때는 가득 찬 체력으로 시작한다).
    public class DisconnectedPlayerStateStore
    {
        public static readonly TimeSpan RetentionTime = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<long, (PlayerStateSnapshot State, DateTime SavedAtUtc)> _states = new();

        public void Save(long playerId, PlayerStateSnapshot state)
        {
            var now = DateTime.UtcNow;
            _states[playerId] = (state, now);

            // 다시 들어오지 않은 캐릭터의 항목이 쌓이지 않도록, 저장할 때마다 보관 기간이 지난 항목을 지운다.
            foreach (var (id, entry) in _states)
            {
                if (now - entry.SavedAtUtc > RetentionTime)
                {
                    _states.TryRemove(new KeyValuePair<long, (PlayerStateSnapshot, DateTime)>(id, entry));
                }
            }
        }

        // 보관 중인 상태를 꺼낸다(꺼내면 지운다 - 한 번의 입장에만 쓴다). 없거나 보관 기간이 지났으면 null.
        public PlayerStateSnapshot? Take(long playerId)
        {
            if (!_states.TryRemove(playerId, out var entry) || DateTime.UtcNow - entry.SavedAtUtc > RetentionTime)
            {
                return null;
            }

            return entry.State;
        }
    }

    // 다시 입장할 때 이어받는 플레이어 상태. 체력이 0이면 사망한 채로 끊긴 것이다. 마나도 체력과 같은 이유로 이어받는다(끊었다 다시
    // 들어오는 것만으로 마나가 가득 차지 않게). CurrentMp가 음수면 "마나 정보 없음"이라 가득 찬 마나로 시작한다.
    public sealed record PlayerStateSnapshot(string MapId, float X, float Y, float Z, float RotationY, int CurrentHp, int CurrentMp = -1);
}
