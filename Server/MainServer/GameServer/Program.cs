using GameServer.Combat;
using GameServer.Logging;
using Microsoft.Extensions.Logging;
using GameServer.Items;
using GameServer.Maps;
using GameServer.Monsters;
using GameServer.Networking;
using Microsoft.Extensions.Configuration;

const int Port = 9000;

// AddEnvironmentVariables가 appsettings.json 값을 덮어쓴다 - docker-compose.yml이 AuthServer__BaseUrl,
// GameServer__TlsCertPath 등을 환경변수로 주입하는 게 바로 이 순서를 전제로 한다(ASP.NET Core의
// Section__Key 관례와 동일). 이 호출이 빠지면 컨테이너 배포 시 appsettings.json의 로컬 개발용 기본값
// (https://localhost:58208 등)이 그대로 쓰여 컨테이너 네트워크 안에서 접속이 실패한다.
IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// 로깅은 카탈로그 로드(EnsureLoaded)보다 먼저 설정해야 로드 로그가 출력된다. 레벨은 appsettings.json 또는 환경변수
// (Logging__LogLevel__Default 등)로 바꾼다 - 이동 거부 같은 잦은 진단 로그는 Debug라 기본값(Information)에서는 보이지 않는다.
using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddConfiguration(configuration.GetSection("Logging"))
    .AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));
GameLog.Configure(loggerFactory);

// 전투 튜닝 값(대쉬 무적, 몬스터 선딜 등)은 appsettings.json의 Combat 섹션으로 조정하며, 잘못된 값이면 여기서 시작을 막는다.
CombatTuning.Configure(configuration);

// 스폰 포인트 파일이 잘못돼 있으면 첫 플레이어가 접속하는 순간이 아니라 여기서 바로 서버 시작을 막는다.
MonsterSpawnCatalog.EnsureLoaded();

// 아이템 정의 -> 드롭 테이블 -> 맵 데이터 순서로 로드해야 한다: 드롭 테이블 검증이 아이템 존재 여부를,
// 맵 데이터(상자) 검증이 드롭 테이블 존재 여부를 각각 대조해야 하기 때문이다.
ItemCatalog.EnsureLoaded();
DropTableCatalog.EnsureLoaded();
MapDataCatalog.EnsureLoaded();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameTcpServer(Port, configuration);
await server.RunAsync(cts.Token);

GameLog.For("GameServer").LogInformation("종료되었습니다.");
