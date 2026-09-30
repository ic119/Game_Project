namespace GameServer.Items
{
    // 한 세션(캐릭터)의 물약 재사용 대기시간. 모든 물약이 하나의 대기시간을 공유하고, 마지막으로 쓴 물약의
    // ItemDefinition.UseCooldownSeconds만큼 다음 물약을 못 쓴다 - 종류별로 따로 두면 소형 -> 중형 -> 대형을 연달아 써서
    // 한순간에 100%를 회복할 수 있어 "회복 속도 상한(회복% / 대기시간)"이 무의미해지기 때문이다.
    //
    // 예약 방식: 물약을 실제로 소모하기 전에 TryReserve로 대기시간을 먼저 걸어 둔다. 소모(MainServer 왕복)가 await 중일 때
    // 같은 세션에서 두 번째 요청이 들어와도 이미 대기 중이라 막히기 때문이다. 소모/회복이 실패하면 Cancel로 되돌려,
    // 실패한 시도가 대기시간을 잡아먹지 않게 한다.
    //
    // 시계를 주입받으므로(기본은 Environment.TickCount64, 밀리초) 테스트에서 시간을 직접 움직일 수 있다.
    public sealed class PotionCooldown
    {
        // TryReserve가 성공하면 돌려주는 값. Cancel이 이전 상태로 복구하는 데 쓴다.
        public readonly record struct Reservation(long PreviousReadyAtMs, long ReadyAtMs);

        private readonly Func<long> _nowMs;
        private readonly object _lock = new();
        private long _readyAtMs;

        public PotionCooldown(Func<long>? nowMs = null)
        {
            _nowMs = nowMs ?? (() => Environment.TickCount64);
        }

        // 대기 중이 아니면 이번 사용분의 대기시간(cooldownSeconds)을 즉시 걸고 true. 아직 대기 중이면 걸지 않고
        // 남은 시간(밀리초)을 remainingMs로 돌려주며 false. cooldownSeconds가 0이어도 다른 물약의 대기 중에는 막힌다.
        public bool TryReserve(float cooldownSeconds, out Reservation reservation, out long remainingMs)
        {
            lock (_lock)
            {
                long now = _nowMs();
                if (now < _readyAtMs)
                {
                    reservation = default;
                    remainingMs = _readyAtMs - now;
                    return false;
                }

                long previous = _readyAtMs;
                _readyAtMs = now + (long)Math.Ceiling(Math.Max(0f, cooldownSeconds) * 1000.0);
                reservation = new Reservation(previous, _readyAtMs);
                remainingMs = 0;
                return true;
            }
        }

        // 소모/회복이 실패해 이번 예약을 물려야 할 때. 그 사이 다른 예약으로 값이 바뀌었으면(있을 수 없는 순서지만) 건드리지 않는다.
        public void Cancel(Reservation reservation)
        {
            lock (_lock)
            {
                if (_readyAtMs == reservation.ReadyAtMs)
                {
                    _readyAtMs = reservation.PreviousReadyAtMs;
                }
            }
        }

        // 지금 남은 대기시간(밀리초). 대기 중이 아니면 0. 클라이언트 표시용 값(3단계)을 만들 때 쓴다.
        public long RemainingMs()
        {
            lock (_lock)
            {
                return Math.Max(0, _readyAtMs - _nowMs());
            }
        }
    }
}
