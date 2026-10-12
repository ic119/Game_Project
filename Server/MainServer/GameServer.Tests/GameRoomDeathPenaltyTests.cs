using GameServer.Monsters;
using GameServer.Networking;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 사망 경험치 패널티: 사망이 확정되는 순간 현재 경험치의 10%(CombatTuning.DeathExpPenaltyPercent 기본값)를 잃고,
// 사망한 본인에게만 Game_ExpGainBroadcast(GainedExp 음수)로 알린다.
public class GameRoomDeathPenaltyTests : IDisposable
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

        public List<S2CExpGainBroadcast> ExpUpdates()
        {
            lock (_sent)
            {
                return _sent.Where(f => f.OpCode == OpCode.Game_ExpGainBroadcast).Select(f => S2CExpGainBroadcast.Decode(f.Body)).ToList();
            }
        }
    }

    private readonly CancellationTokenSource _lifetime = new();

    public void Dispose() => _lifetime.Cancel();

    private static PlayerInfo Player(long id, int hp = 100, int exp = 0) => new()
    {
        PlayerId = id,
        Nickname = $"p{id}",
        MapId = "penalty-test-map",
        MaxHp = 100,
        CurrentHp = hp,
        MaxMp = 50,
        CurrentMp = 50,
        Level = 1,
        Exp = exp,
        AttackPower = 10
    };

    private GameRoom NewRoom() => new("penalty-test-map", new List<MonsterSpawnPointDefinition>(), _lifetime.Token);

    [Fact]
    public void Death_LosesTenPercentOfCurrentExp_AndNotifiesOnlyTheVictim()
    {
        var room = NewRoom();
        var attackerSender = new RecordingSender();
        var victimSender = new RecordingSender();
        room.Join(Player(1), attackerSender);
        PlayerInfo victim = Player(2, hp: 5, exp: 50);
        room.Join(victim, victimSender);

        room.ApplyPlayerAttack(1, 2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.Equal(0, victim.CurrentHp);
        Assert.Equal(45, victim.Exp);
        S2CExpGainBroadcast update = Assert.Single(victimSender.ExpUpdates());
        Assert.Equal(-5, update.GainedExp);
        Assert.Equal(45, update.TotalExp);
        Assert.Equal(1, update.Level);
        Assert.False(update.DidLevelUp);
        Assert.Equal(100, update.ExpToNextLevel); // 1레벨 -> 2레벨 필요 경험치(ExpTable)
        Assert.Empty(attackerSender.ExpUpdates());
    }

    [Fact]
    public void Death_FractionalLossIsRoundedDown_AndNothingSentWhenLossIsZero()
    {
        var room = NewRoom();
        room.Join(Player(1), new RecordingSender());
        var victimSender = new RecordingSender();
        PlayerInfo victim = Player(2, hp: 5, exp: 9); // 9의 10% = 0.9 -> 0
        room.Join(victim, victimSender);

        room.ApplyPlayerAttack(1, 2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.Equal(0, victim.CurrentHp);
        Assert.Equal(9, victim.Exp);
        Assert.Empty(victimSender.ExpUpdates());
    }

    [Fact]
    public void NonLethalHit_DoesNotCostExp()
    {
        var room = NewRoom();
        room.Join(Player(1), new RecordingSender());
        var victimSender = new RecordingSender();
        PlayerInfo victim = Player(2, hp: 100, exp: 50);
        room.Join(victim, victimSender);

        room.ApplyPlayerAttack(1, 2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.True(victim.CurrentHp > 0);
        Assert.Equal(50, victim.Exp);
        Assert.Empty(victimSender.ExpUpdates());
    }

    [Fact]
    public void HittingAnAlreadyDeadPlayer_DoesNotCostExpTwice()
    {
        var room = NewRoom();
        room.Join(Player(1), new RecordingSender());
        var victimSender = new RecordingSender();
        PlayerInfo victim = Player(2, hp: 5, exp: 50);
        room.Join(victim, victimSender);

        room.ApplyPlayerAttack(1, 2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        room.ApplyPlayerAttack(1, 2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.Equal(45, victim.Exp);
        Assert.Single(victimSender.ExpUpdates());
    }
}
