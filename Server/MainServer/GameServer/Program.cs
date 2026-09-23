using GameServer.Items;
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

// 스폰 포인트 파일이 잘못돼 있으면 첫 플레이어가 접속하는 순간이 아니라 여기서 바로 서버 시작을 막는다.
MonsterSpawnCatalog.EnsureLoaded();

// 아이템 정의 -> 드롭 테이블 순서로 로드해야, 드롭 테이블 검증이 아이템 존재 여부를 대조할 수 있다.
ItemCatalog.EnsureLoaded();
DropTableCatalog.EnsureLoaded();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameTcpServer(Port, configuration);
await server.RunAsync(cts.Token);

Console.WriteLine("[GameServer] 종료되었습니다.");
