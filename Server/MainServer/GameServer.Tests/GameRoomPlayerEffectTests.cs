using GameServer.Monsters;
using GameServer.Networking;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 레벨업/물약 사용 연출 알림(Game_PlayerEffectBroadcast): 그 플레이어를 보고 있는 다른 플레이어에게만 가고, 본인과 멀리 있는 플레이어에게는 가지 않는다.
public class GameRoomPlayerEffectTests : IDisposable
{
    private sealed class RecordingSender : ISessionSender
    {
        private readonly List<(OpCode OpCode, byte[] Body)> _sent = new();

        public void Send(OpCode opCode, byte[] body)
        {
            lock (_sent)
            {
                _sent.Add((opCode, body));
            }
        }

        public List<S2CPlayerEffectBroadcast> Effects()
        {
            lock (_sent)
            {
                return _sent.Where(f => f.OpCode == OpCode.Game_PlayerEffectBroadcast).Select(f => S2CPlayerEffectBroadcast.Decode(f.Body)).ToList();
            }
        }
    }

    private readonly CancellationTokenSource _lifetime = new();

    public void Dispose() => _lifetime.Cancel();

    private static PlayerInfo Player(long id, float x = 0f, int hp = 100, int exp = 0) => new()
    {
        PlayerId = id,
        Nickname = $"p{id}",
        MapId = "effect-test-map",
        X = x,
        MaxHp = 100,
        CurrentHp = hp,
        MaxMp = 50,
        CurrentMp = 50,
        Level = 1,
        Exp = exp,
        AttackPower = 10
    };

    [Fact]
    public void HealingPotion_NotifiesNearbyPlayersButNotTheDrinkerOrFarPlayers()
    {
        var room = new GameRoom("effect-test-map", new List<MonsterSpawnPointDefinition>(), _lifetime.Token);
        var drinker = new RecordingSender();
        var nearby = new RecordingSender();
        var far = new RecordingSender();
        room.Join(Player(1, hp: 40), drinker);
        room.Join(Player(2, x: 10f), nearby);
        room.Join(Player(3, x: 500f), far);

        Assert.True(room.TryHealPlayer(1, 30));

        S2CPlayerEffectBroadcast effect = Assert.Single(nearby.Effects());
        Assert.Equal(1, effect.PlayerId);
        Assert.Equal(PlayerEffectType.HpPotion, effect.Effect);
        Assert.Empty(drinker.Effects());
        Assert.Empty(far.Effects());
    }

    [Fact]
    public void HealingPotion_WhenNothingToHeal_SendsNoEffect()
    {
        var room = new GameRoom("effect-test-map", new List<MonsterSpawnPointDefinition>(), _lifetime.Token);
        var nearby = new RecordingSender();
        room.Join(Player(1, hp: 100), new RecordingSender()); // 체력이 가득 참
        room.Join(Player(2, x: 10f), nearby);

        Assert.False(room.TryHealPlayer(1, 30));

        Assert.Empty(nearby.Effects());
    }

    [Fact]
    public void LevelUp_NotifiesNearbyPlayersButNotTheKillerOrFarPlayers()
    {
        var point = new MonsterSpawnPointDefinition
        {
            PointId = "effect-test-point",
            X = 0f,
            Z = 0f,
            MaxAlive = 1,
            RespawnSeconds = 999f,
            SpawnJitterRadius = 0f,
            Entries = new List<MonsterSpawnEntry> { new() { MonsterType = "TestMonster", Weight = 1 } }
        };
        var room = new GameRoom("effect-test-map", new List<MonsterSpawnPointDefinition> { point }, _lifetime.Token);
        var killer = new RecordingSender();
        var nearby = new RecordingSender();
        var far = new RecordingSender();

        // 1레벨에서 다음 레벨까지 100 경험치가 필요하다(ExpTable). 70을 갖고 있다가 TestMonster(50)를 잡으면 레벨업한다.
        var (_, monsters) = room.Join(Player(1, exp: 70), killer);
        room.Join(Player(2, x: 10f), nearby);
        room.Join(Player(3, x: 500f), far);

        MonsterAttackResult result = room.ApplyMonsterAttack(monsters.Single().MonsterId, 1, 5_000_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.True(result.DidLevelUp);
        S2CPlayerEffectBroadcast effect = Assert.Single(nearby.Effects());
        Assert.Equal(1, effect.PlayerId);
        Assert.Equal(PlayerEffectType.LevelUp, effect.Effect);
        Assert.Empty(killer.Effects());
        Assert.Empty(far.Effects());
    }

    [Fact]
    public void KillWithoutLevelUp_SendsNoEffect()
    {
        var point = new MonsterSpawnPointDefinition
        {
            PointId = "effect-test-point",
            X = 0f,
            Z = 0f,
            MaxAlive = 1,
            RespawnSeconds = 999f,
            SpawnJitterRadius = 0f,
            Entries = new List<MonsterSpawnEntry> { new() { MonsterType = "TestMonster", Weight = 1 } }
        };
        var room = new GameRoom("effect-test-map", new List<MonsterSpawnPointDefinition> { point }, _lifetime.Token);
        var nearby = new RecordingSender();
        var (_, monsters) = room.Join(Player(1, exp: 0), new RecordingSender());
        room.Join(Player(2, x: 10f), nearby);

        MonsterAttackResult result = room.ApplyMonsterAttack(monsters.Single().MonsterId, 1, 5_000_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.False(result.DidLevelUp);
        Assert.Empty(nearby.Effects());
    }
}
