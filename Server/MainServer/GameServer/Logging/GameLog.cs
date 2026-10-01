using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameServer.Logging
{
    // GameServer는 DI 컨테이너 없이 정적 카탈로그/세션 객체로 이루어져 있어, ILogger를 생성자로 주입하기 어렵다.
    // 그래서 클래스마다 `private static readonly ILogger Log = GameLog.For<ClientSession>();` 한 줄로 로거를 얻고,
    // 실제 출력 대상(콘솔 등)은 서버 시작 시 Program.cs가 GameLog.Configure로 한 번 정한다.
    //
    // 정적 필드는 Configure보다 먼저 초기화될 수 있으므로(예: 카탈로그 EnsureLoaded), 반환하는 로거는 호출 시점에
    // 현재 팩토리를 찾아 쓰는 지연 로거다 - 설정 전에 만든 로거도 설정 뒤에는 정상 출력된다. 설정 전/테스트 중에는 아무것도
    // 출력하지 않는다(NullLogger).
    public static class GameLog
    {
        private static ILoggerFactory _factory = NullLoggerFactory.Instance;
        private static readonly ConcurrentDictionary<string, ILogger> _inner = new();

        public static void Configure(ILoggerFactory factory)
        {
            _factory = factory;
            _inner.Clear();
        }

        public static ILogger For<T>() => new LazyLogger(typeof(T).FullName ?? typeof(T).Name);

        public static ILogger For(string category) => new LazyLogger(category);

        private static ILogger Resolve(string category) => _inner.GetOrAdd(category, c => _factory.CreateLogger(c));

        private sealed class LazyLogger : ILogger
        {
            private readonly string _category;

            public LazyLogger(string category) => _category = category;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Resolve(_category).BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => Resolve(_category).IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Resolve(_category).Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
