using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Combat;
using GameServer.Maps;
using GameServer.Monsters;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 대쉬 회피: 쿨다운 검증과 무적 구간 기록.
    public partial class GameRoom
    {
        #region Method - Dash
        // 대쉬 회피. 클라이언트가 대쉬를 시작하면 Game_DashRequest로 알리고(ClientSession.HandleDashRequest), 서버는 쿨다운을
        // 검증한 뒤 짧은 무적 구간을 기록한다. 무적 여부는 몬스터 공격 판정(ResolveMonsterAttack)에서만 쓰인다.
        // 클라이언트 값(무적이라는 주장)을 신뢰하지 않고 서버가 요청 시각과 쿨다운으로 직접 정하므로, 변조된 클라이언트가
        // 요청을 도배해도 쿨다운마다 한 번의 짧은 무적밖에 얻지 못한다.

        // 클라이언트 대쉬는 0.25초 지속 + 1초 쿨다운(PlayerMoveController, 시작 간격 1.25초)이다. 무적 시간은 대쉬 지속시간에
        // 패킷 지연 여유를 더한 값이고, 최소 간격은 클라이언트 시작 간격보다 약간 짧게 잡아 시계/지연 오차로 정상 대쉬가
        // 거부되지 않게 한다. 클라이언트 대쉬 값을 바꾸면 함께 조정해야 한다.
        private static TimeSpan DashInvulnerableDuration => TimeSpan.FromSeconds(CombatTuning.Current.DashInvulnerableSeconds);
        private static TimeSpan MinDashInterval => TimeSpan.FromSeconds(CombatTuning.Current.MinDashIntervalSeconds);

        private sealed class DashState
        {
            public long LastDashAtUtcTicks;
            public long InvulnerableUntilUtcTicks;
        }

        // 플레이어 id -> 대쉬 상태. 대쉬 요청(세션 스레드)과 몬스터 공격 판정(방 틱)이 동시에 접근하므로 항목 단위로 lock한다.
        private readonly ConcurrentDictionary<long, DashState> _dashStates = new();

        // 대쉬 시작을 기록하고 무적 구간을 연다. 죽었거나 쿨다운 중이면 false(무적 없음).
        public bool RegisterDash(long playerId)
        {
            if (!_players.TryGetValue(playerId, out var entry) || entry.Info.CurrentHp <= 0)
            {
                return false;
            }

            DashState state = _dashStates.GetOrAdd(playerId, _ => new DashState());
            long now = DateTime.UtcNow.Ticks;

            lock (state)
            {
                if (state.LastDashAtUtcTicks != 0 && now - state.LastDashAtUtcTicks < MinDashInterval.Ticks)
                {
                    return false;
                }

                state.LastDashAtUtcTicks = now;
                state.InvulnerableUntilUtcTicks = now + DashInvulnerableDuration.Ticks;
                return true;
            }
        }

        // 지금 무적 구간 안인지, 그리고 windupStartedAtUtcTicks 이후에 대쉬를 시작했는지(= 이번 몬스터 공격의 선딜 중 대쉬했는지).
        private void GetDashStatus(long playerId, long windupStartedAtUtcTicks, out bool isInvulnerable, out bool dashedDuringWindup)
        {
            isInvulnerable = false;
            dashedDuringWindup = false;

            if (!_dashStates.TryGetValue(playerId, out DashState? state))
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            lock (state)
            {
                isInvulnerable = now < state.InvulnerableUntilUtcTicks;
                dashedDuringWindup = state.LastDashAtUtcTicks >= windupStartedAtUtcTicks;
            }
        }
        #endregion
    }
}
