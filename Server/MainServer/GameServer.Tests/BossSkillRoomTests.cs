using GameServer.Monsters;
using GameServer.Networking;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 보스 스킬(범위 공격/돌진/소환)이 실제 방 틱에서 "예고 -> 발동 -> 판정" 순서로 동작하는지 확인한다. 픽스처 Monsters/MonsterDefinitions.json의
// TestBossSlam/TestBossCharge/TestBossSummon은 스킬을 하나씩만 가진 보스(예고 0.5~0.7초, 재사용 대기 100초)라 한 번의 시전만 관찰된다.
// 방 틱(50ms)은 실제 시간으로 돌아가므로 받은 패킷을 폴링해서 확인한다. 격자가 없는 맵("boss-test-map")이라 이동은 직선이다.
public class BossSkillRoomTests : IDisposable
{
    private sealed class RecordingSender : ISessionSender
    {
        private readonly List<(OpCode OpCode, byte[] Body, DateTime At)> _sent = new();

        public void Send(OpCode opCode, byte[] body)
        {
            lock (_sent)
            {
                _sent.Add((opCode, body, DateTime.UtcNow));
            }
        }

        public List<(OpCode OpCode, byte[] Body, DateTime At)> Snapshot()
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }

        public IEnumerable<T> Decode<T>(OpCode opCode, Func<byte[], T> decode) =>
            Snapshot().Where(f => f.OpCode == opCode).Select(f => decode(f.Body));

        public DateTime? FirstAt(OpCode opCode)
        {
            var found = Snapshot().Where(f => f.OpCode == opCode).Select(f => (DateTime?)f.At).FirstOrDefault();
            return found;
        }
    }

    private readonly CancellationTokenSource _lifetime = new();

    public void Dispose() => _lifetime.Cancel();

    private sealed record Arena(GameRoom Room, RecordingSender Sender, long BossId);

    // 보스(0, 0)와 플레이어 1명(playerX, playerZ)이 있는 방. 플레이어 체력은 충분히 크게 둔다.
    private Arena CreateArena(string bossType, float playerX, float playerZ)
    {
        var point = new MonsterSpawnPointDefinition
        {
            PointId = "boss-test-point",
            X = 0f,
            Z = 0f,
            MaxAlive = 1,
            RespawnSeconds = 999f,

            // 보스가 정확히 (0, 0)에서 시작해야 거리/방향 기대값을 결정적으로 적을 수 있다.
            SpawnJitterRadius = 0f,
            Entries = new List<MonsterSpawnEntry> { new() { MonsterType = bossType, Weight = 1 } }
        };

        var room = new GameRoom("boss-test-map", new List<MonsterSpawnPointDefinition> { point }, _lifetime.Token);
        var sender = new RecordingSender();
        var player = new PlayerInfo
        {
            PlayerId = 1,
            Nickname = "p1",
            MapId = "boss-test-map",
            X = playerX,
            Z = playerZ,
            MaxHp = 1_000_000,
            CurrentHp = 1_000_000,
            AttackPower = 10,
            Defense = 0
        };

        var (_, monsters) = room.Join(player, sender);
        return new Arena(room, sender, monsters.Single().MonsterId);
    }

    private static bool WaitFor(Func<bool> condition, double seconds = 8)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    private static List<S2CBossSkillTelegraphBroadcast> Telegraphs(RecordingSender s) =>
        s.Decode(OpCode.Game_BossSkillTelegraphBroadcast, S2CBossSkillTelegraphBroadcast.Decode).ToList();

    private static List<S2CBossSkillEndBroadcast> Ends(RecordingSender s) =>
        s.Decode(OpCode.Game_BossSkillEndBroadcast, S2CBossSkillEndBroadcast.Decode).ToList();

    private static List<S2CMonsterAttackBroadcast> Hits(RecordingSender s) =>
        s.Decode(OpCode.Game_MonsterAttackBroadcast, S2CMonsterAttackBroadcast.Decode).ToList();

    private static List<S2CMonsterAttackDodgedBroadcast> Dodges(RecordingSender s) =>
        s.Decode(OpCode.Game_MonsterAttackDodgedBroadcast, S2CMonsterAttackDodgedBroadcast.Decode).ToList();

    // 일반 근접 공격 한 방의 피해(공격력 20, 방어 0)보다 큰 피해는 스킬 명중(배율 2.0 = 40)이다.
    private const int NormalAttackDamage = 20;

    // ---------------- 범위 공격 ----------------

    [Fact]
    public void Slam_TelegraphsFirst_ThenHitsPlayerInsideRadius()
    {
        Arena arena = CreateArena("TestBossSlam", 3f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0), "예고(Telegraph)가 오지 않았다.");
        S2CBossSkillTelegraphBroadcast telegraph = Telegraphs(arena.Sender)[0];
        Assert.Equal(arena.BossId, telegraph.MonsterId);
        Assert.Equal((byte)BossSkillType.AreaSlam, telegraph.SkillType);
        Assert.Equal(4f, telegraph.Radius);
        Assert.Equal(700, telegraph.DurationMs);

        // 예고 시점에는 아직 피해가 없다.
        Assert.Empty(Hits(arena.Sender));

        Assert.True(WaitFor(() => Ends(arena.Sender).Count > 0), "발동(End)이 오지 않았다.");
        Assert.True(Ends(arena.Sender)[0].Executed);

        // 예고 시간(0.7초)이 지난 뒤에 발동한다 - 틱 오차를 감안해 0.6초 이상.
        double elapsed = (arena.Sender.FirstAt(OpCode.Game_BossSkillEndBroadcast)!.Value - arena.Sender.FirstAt(OpCode.Game_BossSkillTelegraphBroadcast)!.Value).TotalSeconds;
        Assert.InRange(elapsed, 0.6, 1.2);

        Assert.True(WaitFor(() => Hits(arena.Sender).Count > 0), "범위 안의 플레이어가 맞지 않았다.");
        S2CMonsterAttackBroadcast hit = Hits(arena.Sender)[0];
        Assert.Equal(1, hit.TargetPlayerId);
        Assert.True(hit.Damage > NormalAttackDamage, $"스킬 피해({hit.Damage})가 일반 공격({NormalAttackDamage})보다 커야 한다.");
    }

    [Fact]
    public void Slam_PlayerWhoStaysOutsideRadius_IsNotHit()
    {
        // 플레이어(5, 0)는 시전 범위(6m) 안이지만 피해 반경(4m) 밖이다.
        Arena arena = CreateArena("TestBossSlam", 5f, 0f);

        Assert.True(WaitFor(() => Ends(arena.Sender).Count > 0));
        Thread.Sleep(300); // 발동 직후 판정이 보낼 패킷을 기다린다(후딜 0.5초 동안은 보스가 일반 공격도 하지 않는다).

        Assert.Empty(Hits(arena.Sender));
        Assert.Empty(Dodges(arena.Sender));
    }

    [Fact]
    public void Slam_DashingDuringTelegraph_DodgesTheHit()
    {
        Arena arena = CreateArena("TestBossSlam", 3f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0));

        // 예고가 보인 뒤 0.5초 지점에 대쉬한다 - 무적(0.35초)이 판정 시각(예고 0.7초)을 덮는다.
        Thread.Sleep(500);
        Assert.True(arena.Room.RegisterDash(1));

        Assert.True(WaitFor(() => Dodges(arena.Sender).Count > 0), "대쉬로 피했는데 회피 알림이 오지 않았다.");
        Assert.Equal(arena.BossId, Dodges(arena.Sender)[0].MonsterId);
        Assert.DoesNotContain(Hits(arena.Sender), h => h.Damage > NormalAttackDamage);
    }

    [Fact]
    public void Slam_UsedOnceThenOnCooldown()
    {
        Arena arena = CreateArena("TestBossSlam", 3f, 0f);

        Assert.True(WaitFor(() => Ends(arena.Sender).Count > 0));

        // 후딜(0.5) + 스킬 간격(1.0)이 지나고도 재사용 대기(100초) 때문에 같은 스킬을 또 쓰지 않는다.
        Thread.Sleep(2500);
        Assert.Single(Telegraphs(arena.Sender));
    }

    [Fact]
    public void BossKilledDuringTelegraph_CancelsSkillWithoutHitting()
    {
        Arena arena = CreateArena("TestBossSlam", 3f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0));
        arena.Room.ApplyMonsterAttack(arena.BossId, 1, 5_000_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.True(WaitFor(() => Ends(arena.Sender).Count > 0), "취소(End)가 오지 않았다.");
        Assert.False(Ends(arena.Sender)[0].Executed);

        Thread.Sleep(900); // 예고 시간이 지나도 발동/피해가 없어야 한다.
        Assert.Single(Ends(arena.Sender));
        Assert.Empty(Hits(arena.Sender));
    }

    // ---------------- 돌진 ----------------

    [Fact]
    public void Charge_RunsAlongTelegraphedLineAndHitsPlayerOnIt()
    {
        Arena arena = CreateArena("TestBossCharge", 5f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0));
        S2CBossSkillTelegraphBroadcast telegraph = Telegraphs(arena.Sender)[0];
        Assert.Equal((byte)BossSkillType.Charge, telegraph.SkillType);
        Assert.Equal(2f, telegraph.Width);
        Assert.Equal(8f, telegraph.Length);
        Assert.Equal(90f, telegraph.RotationY, 1); // +X 방향(Atan2(1, 0) = 90도)
        Assert.Equal(0f, telegraph.CenterX, 1);

        Assert.True(WaitFor(() => Hits(arena.Sender).Count > 0), "돌진 경로 위의 플레이어가 맞지 않았다.");
        Assert.True(Hits(arena.Sender)[0].Damage > NormalAttackDamage);

        // 길이 8m를 달려 거의 끝까지 갔다(예고된 방향으로만 이동).
        Assert.True(arena.Room.TryGetMonsterPosition(arena.BossId, out var position));
        Assert.True(WaitFor(() => arena.Room.TryGetMonsterPosition(arena.BossId, out var p) && p.X >= 7.0f), "보스가 예고된 길이만큼 달리지 않았다.");
        Assert.True(arena.Room.TryGetMonsterPosition(arena.BossId, out position));
        Assert.InRange(position.Z, -0.5f, 0.5f);
    }

    [Fact]
    public void Charge_PlayerWhoLeavesTheLineDuringTelegraph_IsNotHit()
    {
        Arena arena = CreateArena("TestBossCharge", 5f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0));

        // 예고가 보인 뒤 옆으로 비킨다 - 돌진 방향은 시전 시작 때 고정됐으므로 보스는 그대로 +X로 달린다.
        arena.Room.UpdatePosition(1, 5f, 0f, 5f, 0f);

        Assert.True(WaitFor(() => arena.Room.TryGetMonsterPosition(arena.BossId, out var p) && p.X >= 7.0f), "보스가 돌진하지 않았다.");
        Assert.DoesNotContain(Hits(arena.Sender), h => h.Damage > NormalAttackDamage);
    }

    [Fact]
    public void Charge_DashingThroughTheLine_Dodges()
    {
        Arena arena = CreateArena("TestBossCharge", 5f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0));

        // 돌진이 시작돼 보스가 플레이어(x=5)에 3m 이내로 다가온 순간 대쉬한다. 보스는 초당 12m로 달리므로 3m -> 접촉 거리(약 1.4m)까지
        // 0.15초쯤 걸리고, 무적(0.35초)이 그 접촉 시각을 덮는다.
        Assert.True(WaitFor(() => Ends(arena.Sender).Count > 0));
        Assert.True(WaitFor(() => arena.Room.TryGetMonsterPosition(arena.BossId, out var p) && 5f - p.X <= 3f), "돌진이 플레이어 쪽으로 오지 않았다.");
        Assert.True(arena.Room.RegisterDash(1));

        Assert.True(WaitFor(() => Dodges(arena.Sender).Count > 0), "대쉬로 돌진을 피했는데 회피 알림이 오지 않았다.");
        Assert.DoesNotContain(Hits(arena.Sender), h => h.Damage > NormalAttackDamage);
    }

    // ---------------- 소환 ----------------

    [Fact]
    public void Summon_TelegraphsPoints_ThenSpawnsRewardlessMinions_ThatVanishWhenBossDies()
    {
        Arena arena = CreateArena("TestBossSummon", 3f, 0f);

        Assert.True(WaitFor(() => Telegraphs(arena.Sender).Count > 0));
        S2CBossSkillTelegraphBroadcast telegraph = Telegraphs(arena.Sender)[0];
        Assert.Equal((byte)BossSkillType.Summon, telegraph.SkillType);
        Assert.Equal(2, telegraph.Points.Count);

        // 소환된 하수인이 일반 몬스터 스폰과 같은 경로(시야 갱신)로 클라이언트에 알려진다. 처음 입장 때 본 보스는 제외한다.
        List<MonsterInfo> Spawned() => arena.Sender.Decode(OpCode.Game_MonsterSpawnBroadcast, b => S2CMonsterSpawnBroadcast.Decode(b).Monster)
            .Where(m => m.MonsterId != arena.BossId).GroupBy(m => m.MonsterId).Select(g => g.First()).ToList();

        Assert.True(WaitFor(() => Spawned().Count >= 2), "하수인 2마리가 나타나지 않았다.");
        List<MonsterInfo> minions = Spawned();
        Assert.All(minions, m =>
        {
            Assert.Equal("TestMonster", m.MonsterType);
            Assert.Equal(0, m.ExpReward); // 하수인은 경험치가 없다
        });

        // 나타난 자리는 예고한 자리와 같다.
        foreach (BossSkillPoint point in telegraph.Points)
        {
            Assert.Contains(minions, m => Math.Abs(m.X - point.X) < 0.01f && Math.Abs(m.Z - point.Z) < 0.01f);
        }

        // 보스가 죽으면 하수인도 함께 사라진다(사망 알림 + 더는 존재하지 않음).
        arena.Room.ApplyMonsterAttack(arena.BossId, 1, 5_000_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        List<long> DiedIds() => arena.Sender.Decode(OpCode.Game_MonsterDieBroadcast, S2CMonsterDieBroadcast.Decode).Select(d => d.MonsterId).ToList();
        Assert.True(WaitFor(() => minions.All(m => DiedIds().Contains(m.MonsterId))), "보스가 죽었는데 하수인에게 사망 알림이 가지 않았다.");
        Assert.Contains(arena.BossId, DiedIds());
        Assert.All(minions, m => Assert.False(arena.Room.TryGetMonsterPosition(m.MonsterId, out _)));
    }

    [Fact]
    public void Summon_NeverExceedsMaxAlive()
    {
        // maxAlive 3, 한 번에 2마리 - 재사용 대기(100초) 때문에 한 번만 소환되므로 정확히 2마리다.
        Arena arena = CreateArena("TestBossSummon", 3f, 0f);

        Assert.True(WaitFor(() => Ends(arena.Sender).Count > 0));
        Thread.Sleep(1500);

        var spawnedIds = arena.Sender.Decode(OpCode.Game_MonsterSpawnBroadcast, b => S2CMonsterSpawnBroadcast.Decode(b).Monster)
            .Where(m => m.MonsterId != arena.BossId).Select(m => m.MonsterId).Distinct().ToList();
        Assert.Equal(2, spawnedIds.Count);
    }
}
