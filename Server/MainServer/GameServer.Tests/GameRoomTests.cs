using GameServer.Networking;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// GameRoom이 ISessionSender에만 의존하게 된 뒤 가능해진 방 단위 테스트. 소켓/ClientSession 없이 가짜 전송기로 보낸 프레임을 모아 검증한다.
// 몬스터 스폰 포인트를 비워 두면 몬스터 없이 플레이어 흐름만 검증할 수 있다. 방 틱(50ms)은 백그라운드로 돌지만
// 이 테스트들이 검증하는 동작은 틱에 의존하지 않는다.
public class GameRoomTests : IDisposable
{
    private sealed class FakeSender : ISessionSender
    {
        private readonly List<(OpCode OpCode, byte[] Body)> _sent = new();

        public IReadOnlyList<OpCode> OpCodes
        {
            get { lock (_sent) { return _sent.Select(f => f.OpCode).ToList(); } }
        }

        public void Send(OpCode opCode, byte[] body)
        {
            lock (_sent)
            {
                _sent.Add((opCode, body));
            }
        }
    }

    private readonly CancellationTokenSource _lifetime = new();

    public void Dispose() => _lifetime.Cancel();

    private GameRoom CreateRoom() => new("test-map", new List<GameServer.Monsters.MonsterSpawnPointDefinition>(), _lifetime.Token);

    private static PlayerInfo Player(long id, float x = 0f, float z = 0f, int attack = 10, int defense = 0, int hp = 100)
        => new()
        {
            PlayerId = id,
            Nickname = $"p{id}",
            MapId = "test-map",
            X = x,
            Z = z,
            MaxHp = hp,
            CurrentHp = hp,
            AttackPower = attack,
            Defense = defense,
        };

    [Fact]
    public void Join_ReturnsOnlyPlayersInsideViewRadius()
    {
        var room = CreateRoom();
        room.Join(Player(1, 0, 0), new FakeSender());
        room.Join(Player(2, 10, 0), new FakeSender());
        room.Join(Player(3, 500, 0), new FakeSender());

        var (visible, _) = room.Join(Player(4, 0, 5), new FakeSender());

        Assert.Equal(new long[] { 1, 2 }, visible.Select(p => p.PlayerId).OrderBy(id => id));
    }

    [Fact]
    public void Remove_WithDifferentSession_KeepsEntry()
    {
        // 같은 캐릭터가 새 세션으로 재입장한 뒤, 이전 세션의 늦은 Remove가 새 항목을 지우면 안 된다.
        var room = CreateRoom();
        var oldSession = new FakeSender();
        var newSession = new FakeSender();
        room.Join(Player(1), oldSession);
        room.Join(Player(1), newSession);

        Assert.False(room.Remove(1, oldSession));
        Assert.True(room.TryGetInfo(1, out _));
        Assert.True(room.Remove(1, newSession));
        Assert.False(room.TryGetInfo(1, out _));
    }

    [Fact]
    public void BroadcastToAll_ReachesEverySession()
    {
        var room = CreateRoom();
        var a = new FakeSender();
        var b = new FakeSender();
        room.Join(Player(1), a);
        room.Join(Player(2), b);

        room.BroadcastToAll(OpCode.Game_PlayerLeft, new S2CPlayerLeft { PlayerId = 9 }.Encode());

        Assert.Contains(OpCode.Game_PlayerLeft, a.OpCodes);
        Assert.Contains(OpCode.Game_PlayerLeft, b.OpCodes);
    }

    [Fact]
    public void ApplyPlayerAttack_ReducesTargetHpAndNotifiesAttacker()
    {
        var room = CreateRoom();
        var attackerSession = new FakeSender();
        room.Join(Player(1, attack: 30), attackerSession);
        room.Join(Player(2, hp: 100), new FakeSender());

        room.ApplyPlayerAttack(1, 2, timestamp: 1);

        Assert.True(room.TryGetInfo(2, out PlayerInfo? target));
        Assert.True(target!.CurrentHp < 100);
        Assert.Contains(OpCode.Game_DamageBroadcast, attackerSession.OpCodes);
    }

    [Fact]
    public void ApplyPlayerAttack_AgainstDeadTarget_IsIgnored()
    {
        var room = CreateRoom();
        var attackerSession = new FakeSender();
        room.Join(Player(1, attack: 1000), attackerSession);
        room.Join(Player(2, hp: 50), new FakeSender());

        room.ApplyPlayerAttack(1, 2, timestamp: 1);
        int damageFramesAfterKill = attackerSession.OpCodes.Count(o => o == OpCode.Game_DamageBroadcast);
        room.ApplyPlayerAttack(1, 2, timestamp: 2);

        Assert.True(room.TryGetInfo(2, out PlayerInfo? target));
        Assert.Equal(0, target!.CurrentHp);
        Assert.Equal(damageFramesAfterKill, attackerSession.OpCodes.Count(o => o == OpCode.Game_DamageBroadcast));
    }

    [Fact]
    public void ApplyPlayerAttack_ByDeadAttacker_IsIgnored()
    {
        var room = CreateRoom();
        room.Join(Player(1, attack: 30, hp: 100), new FakeSender());
        room.Join(Player(2, hp: 100), new FakeSender());
        room.TryGetInfo(1, out PlayerInfo? attacker);
        attacker!.CurrentHp = 0;

        room.ApplyPlayerAttack(1, 2, timestamp: 1);

        room.TryGetInfo(2, out PlayerInfo? target);
        Assert.Equal(100, target!.CurrentHp);
    }

    [Fact]
    public void RegisterDash_RespectsCooldownAndDeath()
    {
        var room = CreateRoom();
        room.Join(Player(1), new FakeSender());
        room.Join(Player(2, hp: 100), new FakeSender());
        room.TryGetInfo(2, out PlayerInfo? dead);
        dead!.CurrentHp = 0;

        Assert.True(room.RegisterDash(1));
        Assert.False(room.RegisterDash(1)); // 쿨다운 안
        Assert.False(room.RegisterDash(2)); // 사망
        Assert.False(room.RegisterDash(999)); // 방에 없음
    }
}
