using GameServer.Monsters;
using GameServer.Networking;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 이동 격자가 있는 맵의 몬스터가 벽을 돌아 추적하는지 방 단위로 확인한다. 픽스처 NavGrids/nav-test-map.json은
// 10m x 6m 방(칸 0.5m)이고, x=5.0~5.5의 세로 벽이 z=0~4를 막고 있으며 z=4~6 구간만 뚫려 있다.
// 몬스터("TestMonster": 감지 5m, 추적 3m/s)는 벽 왼쪽에서 스폰되고 플레이어는 벽 오른쪽에 서 있다 -
// 일직선으로는 벽을 뚫지만, 격자가 있으면 아래쪽 틈을 돌아와야 한다. 방 틱(50ms)은 실제 시간으로 돌아가므로 폴링한다.
public class MonsterNavigationTests : IDisposable
{
    private sealed class FakeSender : ISessionSender
    {
        public void Send(OpCode opCode, byte[] body) { }
    }

    private readonly CancellationTokenSource _lifetime = new();

    public void Dispose() => _lifetime.Cancel();

    private static MonsterSpawnPointDefinition Point() => new()
    {
        PointId = "nav-test-point",
        X = 3.5f,
        Z = 1.0f,
        MaxAlive = 1,
        RespawnSeconds = 999f,
        Entries = new List<MonsterSpawnEntry> { new() { MonsterType = "TestMonster", Weight = 1 } },
        Area = new SpawnArea { CenterX = 5f, CenterZ = 3f, SizeX = 10f, SizeZ = 6f }
    };

    private static PlayerInfo PlayerBehindWall() => new()
    {
        PlayerId = 1,
        Nickname = "p1",
        MapId = "test",
        X = 6.5f,
        Z = 1.0f,
        MaxHp = 100000,
        CurrentHp = 100000,
        AttackPower = 10,
        Defense = 0
    };

    // 몬스터가 플레이어에게 "도달했다"고 보는 거리. 근접 사거리(1.5m)보다 약간 크게 잡는다 - 이 거리면 플레이어(x=6.5)에서
    // 1.7m 이내이므로 x >= 4.8이라, 스폰 위치가 벽 근처(x~4.4)로 뽑혀도 벽 구역(x 4.6~5.9)을 지나야만 도달할 수 있다.
    private const float ReachDistance = 1.7f;

    // 플레이어를 입장시키고 몬스터가 추적을 시작해 플레이어 근처(ReachDistance 안)에 도달하는 동안의 위치를 모두 모은다.
    private List<(float X, float Z)> ChaseAndRecord(string mapId, out bool reachedPlayer)
    {
        var room = new GameRoom(mapId, new List<MonsterSpawnPointDefinition> { Point() }, _lifetime.Token);
        var (_, monsters) = room.Join(PlayerBehindWall(), new FakeSender());
        long monsterId = monsters.Single().MonsterId;

        var positions = new List<(float X, float Z)>();
        reachedPlayer = false;
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            if (room.TryGetMonsterPosition(monsterId, out var position))
            {
                positions.Add((position.X, position.Z));
                float dx = position.X - 6.5f;
                float dz = position.Z - 1.0f;
                if (MathF.Sqrt(dx * dx + dz * dz) <= ReachDistance)
                {
                    reachedPlayer = true;
                    break;
                }
            }

            Thread.Sleep(20);
        }

        return positions;
    }

    // 벽(x 5.0~5.5, z 0~4)과 그 몸통 반경 여유(0.5m) 안으로 들어간 표본이 있는지. 허용 오차 0.1m.
    private static bool EntersWallZone(IEnumerable<(float X, float Z)> positions)
        => positions.Any(p => p.X > 4.6f && p.X < 5.9f && p.Z < 3.9f);

    [Fact]
    public void WithNavGrid_MonsterDetoursAroundWallThroughGap()
    {
        List<(float X, float Z)> positions = ChaseAndRecord("nav-test-map", out bool reached);

        Assert.True(reached, "몬스터가 12초 안에 벽 너머의 플레이어에게 도달하지 못했다.");
        Assert.False(EntersWallZone(positions), "몬스터가 벽을 통과했다.");
        Assert.Contains(positions, p => p.Z > 4.0f); // 아래쪽 틈(z 4~6)을 지나갔다
    }

    [Fact]
    public void WithoutNavGrid_MonsterWalksStraightThroughWall()
    {
        // 대조군: 같은 배치지만 격자가 없는 맵에서는 기존처럼 직선으로 벽을 뚫는다 - 위 테스트가 격자 덕분임을 보인다.
        List<(float X, float Z)> positions = ChaseAndRecord("no-grid-map", out bool reached);

        Assert.True(reached);
        Assert.True(EntersWallZone(positions));
    }
}
