using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using GameServer.Combat;
using GameServer.Items;
using GameServer.Maps;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 전투 요청: 플레이어 공격, 공격 모션 중계, 대쉬, 몬스터 공격.
    public partial class ClientSession
    {
        // 여기서는 위조된 공격자 신원 차단, 최소 공격 간격, 사거리만 검증한다. 피해 계산(방어력 적용)과
        // 대상 HP 갱신/사망 처리는 GameRoom.ApplyPlayerAttack이 서버 권위로 수행한다.
        // 클라이언트 PlayerAttackController.comboInputGuard(150ms)가 지나면 2타 콤보 입력을 즉시 받아들여
        // 두 번째 Game_AttackRequest/Game_MonsterAttackRequest를 보낸다. 이 값이 그보다 크면(과거 300ms)
        // 정상적인 콤보 2타 요청까지 여기서 조용히 드롭되어 "애니메이션은 2콤보, 데미지는 1타"만 반영되는
        // 문제가 생기므로, comboInputGuard보다 여유를 두고 짧게 잡아 정상 콤보는 통과시키고 그보다
        // 빠른(매크로 등) 연타만 차단한다. comboInputGuard를 바꾸면 이 값도 함께 맞춰야 한다.
        private DateTime _lastAttackAtUtc = DateTime.MinValue;

        private void HandleAttackRequest(byte[] body)
        {
            var request = C2SAttackRequest.Decode(body);

            if (_playerId is not { } playerId || request.AttackerId != playerId || request.TargetId == playerId
                || _room is not { } room)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastAttackAtUtc).TotalMilliseconds < CombatTuning.Current.MinAttackIntervalMs)
            {
                return;
            }
            _lastAttackAtUtc = now;

            if (!room.TryGetInfo(playerId, out var attacker) || !room.TryGetInfo(request.TargetId, out var target))
            {
                return;
            }

            float dx = attacker.X - target.X;
            float dy = attacker.Y - target.Y;
            float dz = attacker.Z - target.Z;
            if (dx * dx + dy * dy + dz * dz > CombatTuning.Current.MaxAttackRangeSquared)
            {
                return;
            }

            room.ApplyPlayerAttack(playerId, request.TargetId, request.Timestamp);
        }

        // 데미지/쿨다운 판정 없이 그대로 중계만 한다. Game_AttackRequest와 같은 쿨다운(_lastAttackAtUtc)을
        // 적용하면 안 된다 - 이 요청은 대상이 없는 허공 스윙을 포함해 콤보 타수마다 항상 오므로, 공유 쿨다운을
        // 적용하면 실제 로컬 콤보 타이밍과 어긋난다. 순전히 연출용이라 위조돼도 다른 플레이어 화면에 잘못된
        // 모션이 보이는 것 이상의 피해가 없다.
        private void HandleAttackAnimationRequest(byte[] body)
        {
            var request = C2SAttackAnimationRequest.Decode(body);

            if (_playerId is not { } playerId || request.AttackerId != playerId || _room is not { } room)
            {
                return;
            }

            room.BroadcastAttackAnimation(playerId, request.ComboStage, request.WeaponType);
        }

        // 대쉬 시작 알림. 무적 구간은 서버가 쿨다운을 검증해 정하므로(GameRoom.RegisterDash) 요청을 도배해도 얻는 것이 없지만,
        // 불필요한 처리를 막기 위해 세션별로 빈도도 제한한다. 클라이언트 대쉬는 1.25초 간격이라 이 한도는 정상 입력에 충분하다.
        private readonly RequestRateLimiter _dashRateLimiter = new(capacity: 3, refillPerSecond: 1);

        private void HandleDashRequest(byte[] body)
        {
            if (!_dashRateLimiter.TryAcquire())
            {
                return;
            }

            var request = C2SDashRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

            room.RegisterDash(playerId);
        }

        // 플레이어 공격(HandleAttackRequest)과 같은 쿨다운(_lastAttackAtUtc)을 공유한다 - 그렇지 않으면
        // 플레이어 공격과 몬스터 공격 요청을 번갈아 보내 최소 공격 간격 제한을 우회할 수 있다.
        // 데미지 계산 자체(공격력-방어력)는 GameRoom.ApplyMonsterAttack이 서버 권위로 수행한다.
        // 사망한 플레이어는 몬스터를 공격할 수 없다(PvP는 GameRoom.ApplyPlayerAttack이 같은 검사를 한다).
        private void HandleMonsterAttackRequest(byte[] body)
        {
            var request = C2SMonsterAttackRequest.Decode(body);

            if (_playerId is not { } playerId || request.AttackerId != playerId || _room is not { } room)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastAttackAtUtc).TotalMilliseconds < CombatTuning.Current.MinAttackIntervalMs)
            {
                return;
            }
            _lastAttackAtUtc = now;

            if (!room.TryGetInfo(playerId, out var attacker) || attacker.CurrentHp <= 0
                || !room.TryGetMonsterPosition(request.MonsterId, out var monsterPosition))
            {
                return;
            }

            float dx = attacker.X - monsterPosition.X;
            float dy = attacker.Y - monsterPosition.Y;
            float dz = attacker.Z - monsterPosition.Z;
            if (dx * dx + dy * dy + dz * dz > CombatTuning.Current.MaxAttackRangeSquared)
            {
                return;
            }

            MonsterAttackResult result = room.ApplyMonsterAttack(request.MonsterId, playerId, attacker.AttackPower, request.Timestamp);

            // 방 전체가 아니라 처치자 본인에게만 보낸다 - 다른 접속자는 이 몬스터를 잡은 게 아니므로 경험치와 무관하다.
            if (result is { MonsterDied: true, GainedExp: { } gainedExp })
            {
                var expGain = new S2CExpGainBroadcast
                {
                    MonsterId = request.MonsterId,
                    GainedExp = gainedExp,
                    TotalExp = attacker.Exp,
                    Level = result.NewLevel,
                    DidLevelUp = result.DidLevelUp,
                    ExpToNextLevel = result.ExpToNextLevel
                };
                Send(OpCode.Game_ExpGainBroadcast, expGain.Encode());
            }

            // 골드/아이템도 처치자 본인에게만 보낸다. 만렙이라 GainedExp가 없는 경우에도 드롭은 지급되므로
            // 위 exp 분기와 독립적으로 판단한다(둘 다 0/빈 목록이면 굳이 빈 패킷을 보내지 않는다).
            if (result.MonsterDied && (result.GainedGold > 0 || result.DroppedItems is { Count: > 0 }))
            {
                var loot = new S2CLootBroadcast
                {
                    MonsterId = request.MonsterId,
                    GoldGained = result.GainedGold,
                    Items = result.DroppedItems?.ToList() ?? new List<(string, int)>()
                };
                Send(OpCode.Game_LootBroadcast, loot.Encode());
            }

            // 위 두 패킷은 클라이언트 화면 표시용일 뿐이고, DB 저장은 GameServer가 MainServer 서버 간 API로 직접 한다 -
            // 클라이언트가 저장을 대신 요청하던 방식은 보상 값을 위조할 수 있었다(MainServerInternalApi 참고).
            // 저장은 기다리지 않고 KillRewardSaver에 맡긴다. 캐릭터별로 처치 순서대로 저장하고 실패하면 다시 보내며,
            // 이 세션이 끊겨도 계속 진행한다 - 최종값인 level/exp가 이전 값으로 덮어써지지 않는다.
            bool hasReward = result.GainedExp is not null || result.GainedGold > 0 || result.DroppedItems is { Count: > 0 };
            if (result.MonsterDied && hasReward)
            {
                _killRewardSaver.Enqueue(
                    playerId,
                    attacker.Level,
                    attacker.Exp,
                    result.GainedGold,
                    result.DroppedItems ?? Array.Empty<(string ItemId, int Qty)>());
            }
        }
    }
}
