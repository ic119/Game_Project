using System.Collections.Concurrent;

namespace MainServer.AuthServer.Services
{
    // 계정(아이디)별 연속 로그인 실패를 세어, MaxConsecutiveFailures번 연속 틀리면 LockDuration 동안 그 계정의 로그인을 막는다.
    // IP별 요청 제한(Program.cs의 "auth" 정책)만으로는 여러 IP에서 한 계정의 비밀번호를 대입하는 것을 막지 못한다.
    // 잠금이 짧은 이유: 남의 아이디로 일부러 틀려서 그 사람을 계속 못 들어오게 하는 방해를 오래 가지 못하게 하기 위함이다.
    // 서버 메모리에만 두므로 재시작하면 초기화된다(싱글턴으로 등록).
    public class LoginAttemptTracker
    {
        private const int MaxConsecutiveFailures = 5;
        private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<string, Attempt> _attempts = new(StringComparer.OrdinalIgnoreCase);

        public bool IsLocked(string username)
        {
            return _attempts.TryGetValue(username, out Attempt? attempt) && attempt.LockedUntilUtc > DateTime.UtcNow;
        }

        public void RecordFailure(string username)
        {
            var now = DateTime.UtcNow;
            Attempt attempt = _attempts.GetOrAdd(username, _ => new Attempt());
            lock (attempt)
            {
                // 잠금이 풀린 뒤 다시 틀리기 시작하면 처음부터 센다.
                if (attempt.LockedUntilUtc != DateTime.MinValue && attempt.LockedUntilUtc <= now)
                {
                    attempt.Failures = 0;
                    attempt.LockedUntilUtc = DateTime.MinValue;
                }

                attempt.Failures++;
                attempt.LastFailureUtc = now;
                if (attempt.Failures >= MaxConsecutiveFailures)
                {
                    attempt.LockedUntilUtc = now + LockDuration;
                }
            }

            RemoveStaleEntries(now);
        }

        public void RecordSuccess(string username)
        {
            _attempts.TryRemove(username, out _);
        }

        // 실패만 하고 다시 오지 않는 아이디가 쌓이지 않도록, 마지막 실패 후 오래 지난 항목을 지운다(진행 중인 대입의 횟수는 유지된다).
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

        private void RemoveStaleEntries(DateTime now)
        {
            if (_attempts.Count < 1000)
            {
                return;
            }

            foreach (var (username, attempt) in _attempts)
            {
                if (now - attempt.LastFailureUtc > StaleAfter)
                {
                    _attempts.TryRemove(new KeyValuePair<string, Attempt>(username, attempt));
                }
            }
        }

        private sealed class Attempt
        {
            public int Failures;
            public DateTime LastFailureUtc;
            public DateTime LockedUntilUtc = DateTime.MinValue;
        }
    }

    // 연속 실패로 잠긴 계정의 로그인 요청(컨트롤러가 429로 바꾼다).
    public class LoginLockedException : Exception
    {
        public LoginLockedException() : base("로그인 시도가 너무 많습니다. 잠시 후 다시 시도해 주세요.") { }
    }
}
