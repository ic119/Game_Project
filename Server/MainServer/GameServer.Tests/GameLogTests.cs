using GameServer.Logging;
using Microsoft.Extensions.Logging;

namespace GameServer.Tests;

public class GameLogTests
{
    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingProvider _owner;
            private readonly string _category;

            public CapturingLogger(CapturingProvider owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_owner.Entries)
                {
                    _owner.Entries.Add((_category, logLevel, formatter(state, exception), exception));
                }
            }
        }
    }

    // 정적 필드는 서버 시작 시 Configure보다 먼저 초기화될 수 있다(카탈로그 EnsureLoaded 등). 그때 만든 로거도 이후 설정된 출력으로
    // 나가야 하고, 설정 전에는 조용히 버려져야 한다. GameLog는 정적 전역이라 Configure를 부르는 테스트는 이 하나뿐이어야 한다.
    [Fact]
    public void LoggerCreatedBeforeConfigure_WritesToFactoryConfiguredLater()
    {
        ILogger early = GameLog.For("GameLogTests.Early");
        early.LogInformation("설정 전 로그는 버려진다"); // 예외 없이 무시되어야 한다.

        var provider = new CapturingProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        GameLog.Configure(factory);

        early.LogWarning("레벨업 (PlayerId={PlayerId})", 7L);
        early.LogError(new InvalidOperationException("boom"), "방 틱 오류 ({MapId})", "Floor001");

        lock (provider.Entries)
        {
            var mine = provider.Entries.Where(e => e.Category == "GameLogTests.Early").ToList();
            Assert.Equal(2, mine.Count);
            Assert.Equal((LogLevel.Warning, "레벨업 (PlayerId=7)"), (mine[0].Level, mine[0].Message));
            Assert.Equal(LogLevel.Error, mine[1].Level);
            Assert.IsType<InvalidOperationException>(mine[1].Exception);
        }
    }
}
