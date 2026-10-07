using GameServer.Skills;
using Microsoft.Extensions.Logging;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 액티브 스킬 요청: 시전 승인은 GameRoom.TryBeginSkillCast가 하고, 여기서는 결과 전달과 타격 시점 예약을 맡는다.
    public partial class ClientSession
    {
        // 슬롯 4개를 번갈아 눌러도 정상 입력으로는 넘지 않는 한도. 시전 잠금/쿨다운이 따로 있어 이 제한은 요청 도배(패킷 폭주)만 막는다.
        private readonly RequestRateLimiter _skillRateLimiter = new(capacity: 4, refillPerSecond: 3);

        private void HandleSkillRequest(byte[] body)
        {
            if (!_skillRateLimiter.TryAcquire())
            {
                return;
            }

            var request = C2SSkillRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

            GameRoom.SkillCastResult cast = room.TryBeginSkillCast(playerId, request.Slot, request.RotationY);

            var result = new S2CSkillResult
            {
                Slot = request.Slot,
                Status = cast.Status,
                CooldownSeconds = cast.CooldownSeconds
            };
            Send(OpCode.Game_SkillResult, result.Encode());

            if (!cast.Accepted || cast.Skill is null)
            {
                return;
            }

            room.BroadcastSkillCast(playerId, request.Slot, cast);

            // 타격은 시전 모션이 맞는 시점(HitDelaySeconds)에 일어난다. 기다리지 않고 흘려보낸다(fire-and-forget).
            _ = RunSkillHitsAsync(room, playerId, cast);
        }

        // 스킬의 타격들을 시간에 맞춰 적용한다. 타격마다 그 시점의 몬스터 위치로 범위 안 대상을 다시 찾는다(시전자 위치/방향은 시전 시점 값).
        // 시전자가 죽거나 방을 떠나면 남은 타격은 취소한다.
        private async Task RunSkillHitsAsync(GameRoom room, long playerId, GameRoom.SkillCastResult cast)
        {
            SkillDefinition skill = cast.Skill!;

            try
            {
                for (int hit = 0; hit < skill.Hits; hit++)
                {
                    float delay = hit == 0 ? skill.HitDelaySeconds : skill.HitIntervalSeconds;
                    if (delay > 0f)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay));
                    }

                    // 시전이 대쉬로 취소됐거나 새 시전으로 대체됐으면 남은 타격을 하지 않는다.
                    if (!ReferenceEquals(_room, room) || _playerId != playerId || !room.IsCastCurrent(playerId, cast.CastSerial)
                        || !room.TryGetInfo(playerId, out var attacker) || attacker.CurrentHp <= 0)
                    {
                        return;
                    }

                    int attackPower = GameRoom.CalculateSkillAttackPower(attacker.AttackPower, skill);
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    foreach (long monsterId in room.FindSkillTargets(skill, cast.X, cast.Z, cast.RotationY))
                    {
                        MonsterAttackResult result = room.ApplyMonsterAttack(monsterId, playerId, attackPower, timestamp);
                        ProcessMonsterAttackResult(monsterId, playerId, attacker, result);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "스킬 타격 처리 오류: PlayerId={PlayerId}, Skill={SkillId}", playerId, skill.Id);
            }
        }
    }
}
