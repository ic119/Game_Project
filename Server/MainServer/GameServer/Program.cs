using GameServer.Monsters;
using GameServer.Networking;
using Microsoft.Extensions.Configuration;

const int Port = 9000;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

// 스폰 포인트 파일이 잘못돼 있으면 첫 플레이어가 접속하는 순간이 아니라 여기서 바로 서버 시작을 막는다.
MonsterSpawnCatalog.EnsureLoaded();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameTcpServer(Port, configuration);
await server.RunAsync(cts.Token);

Console.WriteLine("[GameServer] 종료되었습니다.");
