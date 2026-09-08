using GameServer.Networking;

const int Port = 9000;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameTcpServer(Port);
await server.RunAsync(cts.Token);

Console.WriteLine("[GameServer] 종료되었습니다.");
