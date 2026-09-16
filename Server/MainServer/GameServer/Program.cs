using GameServer.Networking;
using Microsoft.Extensions.Configuration;

const int Port = 9000;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameTcpServer(Port, configuration);
await server.RunAsync(cts.Token);

Console.WriteLine("[GameServer] 종료되었습니다.");
