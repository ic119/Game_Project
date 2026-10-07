using GameServer.Monsters;
using GameServer.Networking;
using GameServer.Skills;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 스킬 시전 승인(해금 레벨/마나/쿨다운/시전 잠금), 범위 안 대상 찾기, 시전 모션 중계. 타격 시점 적용은 ClientSession이 맡아 여기서는 다루지 않는다.
// 픽스처 Items/ItemDefinitions.json의 test_weapon_ohs는 한손검, test_weapon_common은 종류 미지정 무기다.
public class GameRoomSkillTests : IDisposable
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

    private GameRoom CreateRoom() => new("test-map", new List<MonsterSpawnPointDefinition>(), _lifetime.Token);

    private static PlayerInfo Caster(long id, int level = 12, int mana = 100, string weapon = "test_weapon_ohs", float x = 0f, float z = 0f)
        => new()
        {
            PlayerId = id,
            Nickname = $"p{id}",
            MapId = "test-map",
            X = x,
            Z = z,
            MaxHp = 100,
            CurrentHp = 100,
            MaxMp = 100,
            CurrentMp = mana,
            Level = level,
            AttackPower = 10,
            WeaponItemId = weapon
        };

    [Fact]
    public void Accepted_SpendsManaAndStartsCooldown()
    {
        var room = CreateRoom();
        var sender = new FakeSender();
        PlayerInfo info = Caster(1);
        room.Join(info, sender);
        SkillCatalog.TryGet(WeaponKind.OneHanded, 1, out SkillDefinition skill);

        GameRoom.SkillCastResult result = room.TryBeginSkillCast(1, 1, rotationY: 30f);

        Assert.Equal(SkillCastStatus.Accepted, result.Status);
        Assert.Equal(skill.Id, result.Skill!.Id);
        Assert.Equal(WeaponKind.OneHanded, result.Weapon);
        Assert.Equal(30f, result.RotationY);
        Assert.Equal(100 - skill.ManaCost, info.CurrentMp);
        Assert.Contains(OpCode.Game_PlayerMpUpdate, sender.OpCodes);
    }

    [Fact]
    public void Rejects_WhenLevelIsBelowUnlockLevel_AndKeepsMana()
    {
        var room = CreateRoom();
        PlayerInfo info = Caster(1, level: 5);
        room.Join(info, new FakeSender());

        Assert.Equal(SkillCastStatus.Accepted, room.TryBeginSkillCast(1, 1, 0f).Status); // Lv3 해금
        Thread.Sleep(700);
        int manaAfterFirst = info.CurrentMp;

        Assert.Equal(SkillCastStatus.LevelTooLow, room.TryBeginSkillCast(1, 2, 0f).Status); // Lv6 해금
        Assert.Equal(SkillCastStatus.LevelTooLow, room.TryBeginSkillCast(1, 4, 0f).Status); // Lv12 해금
        Assert.Equal(manaAfterFirst, info.CurrentMp);
    }

    [Fact]
    public void Rejects_WithoutMatchingWeaponOrSlot()
    {
        var room = CreateRoom();
        room.Join(Caster(1, weapon: "test_weapon_common"), new FakeSender()); // 종류 미지정 무기
        room.Join(Caster(2, weapon: string.Empty), new FakeSender());         // 맨손
        room.Join(Caster(3), new FakeSender());

        Assert.Equal(SkillCastStatus.NoSkill, room.TryBeginSkillCast(1, 1, 0f).Status);
        Assert.Equal(SkillCastStatus.NoSkill, room.TryBeginSkillCast(2, 1, 0f).Status);
        Assert.Equal(SkillCastStatus.NoSkill, room.TryBeginSkillCast(3, 0, 0f).Status);
        Assert.Equal(SkillCastStatus.NoSkill, room.TryBeginSkillCast(3, 5, 0f).Status);
    }

    [Fact]
    public void Rejects_InvalidRequests_AndDeadCasters()
    {
        var room = CreateRoom();
        PlayerInfo dead = Caster(1);
        dead.CurrentHp = 0;
        room.Join(dead, new FakeSender());
        room.Join(Caster(2), new FakeSender());

        Assert.Equal(SkillCastStatus.Dead, room.TryBeginSkillCast(1, 1, 0f).Status);
        Assert.Equal(SkillCastStatus.InvalidRequest, room.TryBeginSkillCast(999, 1, 0f).Status);
        Assert.Equal(SkillCastStatus.InvalidRequest, room.TryBeginSkillCast(2, 1, float.NaN).Status);
        Assert.Equal(SkillCastStatus.InvalidRequest, room.TryBeginSkillCast(2, 1, float.PositiveInfinity).Status);
    }

    [Fact]
    public void Rejects_WhenManaIsInsufficient_WithoutStartingCooldown()
    {
        var room = CreateRoom();
        PlayerInfo info = Caster(1, mana: 5);
        room.Join(info, new FakeSender());

        Assert.Equal(SkillCastStatus.NotEnoughMana, room.TryBeginSkillCast(1, 1, 0f).Status);
        Assert.Equal(5, info.CurrentMp);

        info.CurrentMp = 100;
        Assert.Equal(SkillCastStatus.Accepted, room.TryBeginSkillCast(1, 1, 0f).Status); // 거부는 쿨다운을 남기지 않는다
    }

    [Fact]
    public void Rejects_AnotherSkillDuringCastLock_ThenReportsCooldown()
    {
        var room = CreateRoom();
        room.Join(Caster(1), new FakeSender());

        Assert.Equal(SkillCastStatus.Accepted, room.TryBeginSkillCast(1, 1, 0f).Status);
        Assert.Equal(SkillCastStatus.Casting, room.TryBeginSkillCast(1, 2, 0f).Status); // 시전 모션 중

        Thread.Sleep(700); // 슬롯 1 시전 잠금(0.6초 - 여유 0.1초)이 끝난 뒤

        GameRoom.SkillCastResult again = room.TryBeginSkillCast(1, 1, 0f);
        Assert.Equal(SkillCastStatus.OnCooldown, again.Status);
        Assert.InRange(again.CooldownSeconds, 3.5f, 5f);

        Assert.Equal(SkillCastStatus.Accepted, room.TryBeginSkillCast(1, 2, 0f).Status); // 다른 슬롯은 따로 쿨다운
    }

    [Fact]
    public void BroadcastSkillCast_ReachesViewersButNotTheCasterOrFarPlayers()
    {
        var room = CreateRoom();
        var caster = new FakeSender();
        var nearby = new FakeSender();
        var far = new FakeSender();
        room.Join(Caster(1), caster);
        room.Join(Caster(2, x: 10f), nearby);
        room.Join(Caster(3, x: 500f), far);

        GameRoom.SkillCastResult cast = room.TryBeginSkillCast(1, 1, 45f);
        room.BroadcastSkillCast(1, 1, cast);

        Assert.Single(nearby.OpCodes, OpCode.Game_SkillCastBroadcast);
        Assert.DoesNotContain(OpCode.Game_SkillCastBroadcast, caster.OpCodes);
        Assert.DoesNotContain(OpCode.Game_SkillCastBroadcast, far.OpCodes);
    }

    [Fact]
    public void FindSkillTargets_ReturnsAliveMonstersInsideShape_NearestFirst_CappedByMaxTargets()
    {
        var point = new MonsterSpawnPointDefinition
        {
            PointId = "skill-test-point",
            X = 0f,
            Z = 0f,
            MaxAlive = 3,
            RespawnSeconds = 999f,
            SpawnJitterRadius = 0f,
            Entries = new List<MonsterSpawnEntry> { new() { MonsterType = "TestMonster", Weight = 1 } }
        };
        var room = new GameRoom("skill-test-map", new List<MonsterSpawnPointDefinition> { point }, _lifetime.Token);

        // 몬스터는 감지 범위(5m) 밖인 (30, 0)의 플레이어에게는 반응하지 않고 스폰 지점(0, 0)에 모여 있다.
        PlayerInfo watcher = Caster(1, x: 30f);
        watcher.MapId = "skill-test-map";
        var (_, monsters) = room.Join(watcher, new FakeSender());
        Assert.Equal(3, monsters.Count);

        SkillCatalog.TryGet(WeaponKind.OneHanded, 2, out SkillDefinition whirlwind); // 자신 중심 반지름 2.5m 원

        List<long> nearOrigin = room.FindSkillTargets(whirlwind, 0f, 0f, 0f);
        Assert.Equal(3, nearOrigin.Count);
        Assert.Equal(nearOrigin.Count, nearOrigin.Distinct().Count());

        Assert.Empty(room.FindSkillTargets(whirlwind, 20f, 20f, 0f)); // 범위 밖

        SkillCatalog.TryGet(WeaponKind.OneHanded, 1, out SkillDefinition pierce); // 직선 3.5m
        Assert.Equal(3, room.FindSkillTargets(pierce, 0f, -2f, 0f).Count);  // 앞쪽으로 향하면 맞는다
        Assert.Empty(room.FindSkillTargets(pierce, 0f, -2f, 180f));         // 반대쪽을 보면 맞지 않는다
    }

    [Theory]
    [InlineData(10, 1.8f, 18)]
    [InlineData(10, 1.5f, 15)]
    [InlineData(7, 2.5f, 18)]   // 17.5 -> 반올림(짝수 쪽 반올림이 아닌 MathF.Round 기본: 18)
    [InlineData(0, 3.5f, 1)]    // 최소 1
    public void CalculateSkillAttackPower_ScalesAndRoundsWithMinimumOne(int attack, float multiplier, int expected)
    {
        var skill = new SkillDefinition { DamageMultiplier = multiplier };

        Assert.Equal(expected, GameRoom.CalculateSkillAttackPower(attack, skill));
    }
}
