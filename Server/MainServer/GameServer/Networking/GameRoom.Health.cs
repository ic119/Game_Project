using GameServer.Logging;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Combat;
using GameServer.Maps;
using GameServer.Monsters;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 플레이어 HP(서버 권위): 피해/PvP/회복/레벨업/사망 후 부활.
    public partial class GameRoom
    {
        #region Method - Player Health
        // 플레이어 HP는 서버(이 GameRoom)가 유일한 권위다. 피해/레벨업/사망/부활은 모두 여기서 계산하고,
        // 클라이언트에는 결과(최종 피해량, 남은 체력)만 보낸다 - 클라이언트가 계산하던 예전 방식은
        // 피격을 무시하는 변조 클라이언트가 무적이 될 수 있었고, 서버/클라이언트 HP가 서로 어긋났다.
        // HP는 AI 루프(몬스터 공격)와 여러 세션(PvP)이 동시에 바꿀 수 있으므로 PlayerInfo 인스턴스를 lock으로 쓴다.

        // 사망 후 자동 부활까지 걸리는 시간. 클라이언트 부활 팝업의 카운트다운(GameSceneManager.ReviveDelaySeconds)과 같아야 한다.
        private static readonly TimeSpan ReviveDelay = TimeSpan.FromSeconds(5);

        // target에게 방어력 적용 전 피해(rawDamage)를 준다. 이미 사망한 대상이면 false(피해 없음).
        // died는 이 피해로 체력이 0이 된 경우에만 true다 - 사망 처리(부활 예약)가 한 번만 일어나게 한다.
        private static bool TryDamagePlayer(PlayerInfo target, int rawDamage, out int finalDamage, out int remainingHp, out bool died)
        {
            lock (target)
            {
                if (target.CurrentHp <= 0)
                {
                    finalDamage = 0;
                    remainingHp = 0;
                    died = false;
                    return false;
                }

                finalDamage = CombatStatCalculator.ApplyDefense(rawDamage, target.Defense);
                target.CurrentHp = Math.Max(0, target.CurrentHp - finalDamage);
                remainingHp = target.CurrentHp;
                died = remainingHp <= 0;
                return true;
            }
        }

        // PvP 공격. 사거리/쿨다운은 ClientSession이 검증한 뒤 호출한다. 공격자나 대상이 이미 사망했으면 무시한다.
        public void ApplyPlayerAttack(long attackerId, long targetId, long timestamp)
        {
            if (!_players.TryGetValue(attackerId, out var attackerEntry) || attackerEntry.Info.CurrentHp <= 0
                || !_players.TryGetValue(targetId, out var targetEntry))
            {
                return;
            }

            if (!TryDamagePlayer(targetEntry.Info, attackerEntry.Info.AttackPower, out int finalDamage, out int remainingHp, out bool died))
            {
                return;
            }

            var broadcast = new S2CDamageBroadcast
            {
                AttackerId = attackerId,
                TargetId = targetId,
                Damage = finalDamage,
                RemainingHp = remainingHp,
                Timestamp = timestamp
            };
            SendToViewersOfPlayer(targetId, OpCode.Game_DamageBroadcast, broadcast.Encode(), alsoToPlayerId: attackerId);

            if (died)
            {
                _ = ReviveAfterDelayAsync(targetEntry.Info);
            }
        }

        // 공격 모션 중계 전용 - 데미지/쿨다운 판정은 하지 않는다(ClientSession.HandleAttackAnimationRequest가
        // attackerId 위조만 막고 그대로 넘긴다). attackerId 본인은 이미 로컬에서 재생했으므로 보내지 않는다.
        // weaponType도 공격자가 보낸 값을 그대로 중계한다(서버는 해석하지 않음).
        public void BroadcastAttackAnimation(long attackerId, int comboStage, int weaponType)
        {
            var broadcast = new S2CAttackAnimationBroadcast { AttackerId = attackerId, ComboStage = comboStage, WeaponType = weaponType };
            SendToViewersOfPlayer(attackerId, OpCode.Game_AttackAnimationBroadcast, broadcast.Encode());
        }

        // 회복 아이템을 써도 효과가 있는 상태인지(살아 있고 체력이 가득 차지 않음). 아이템을 차감하기 전에 확인해
        // "효과 없는 사용"으로 아이템만 사라지는 것을 막는다.
        public bool CanBeHealed(long playerId)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            lock (entry.Info)
            {
                return entry.Info.CurrentHp > 0 && entry.Info.CurrentHp < entry.Info.MaxHp;
            }
        }

        // 최대 체력의 healPercent%만큼 회복하고 본인과 주변(시야 안) 플레이어에게 새 체력을 알린다. 그 사이 사망했거나 이미 가득 찼으면 false.
        public bool TryHealPlayer(long playerId, int healPercent)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            PlayerInfo player = entry.Info;
            int currentHp;
            int maxHp;
            lock (player)
            {
                if (player.CurrentHp <= 0 || player.CurrentHp >= player.MaxHp)
                {
                    return false;
                }

                player.CurrentHp = Math.Min(player.MaxHp, player.CurrentHp + CombatStatCalculator.CalculateHealAmount(player.MaxHp, healPercent));
                currentHp = player.CurrentHp;
                maxHp = player.MaxHp;
            }

            var broadcast = new S2CPlayerHpBroadcast { PlayerId = playerId, CurrentHp = currentHp, MaxHp = maxHp };
            SendToViewersOfPlayer(broadcast.PlayerId, OpCode.Game_PlayerHpBroadcast, broadcast.Encode());
            return true;
        }

        // 레벨업 시 공격력/방어력/최대 체력을 새 레벨 기준으로 다시 계산하고 체력을 가득 채운다(PlayerCombatStats.ApplyLevelUp).
        // player.Level은 호출 전에 이미 새 레벨로 바뀌어 있다. 공격력/방어력은 서버 메모리의 PlayerInfo 값이라 이 호출이 끝나는 순간부터
        // 다음 몬스터 판정/피해 계산이 바로 새 값을 쓴다. 클라이언트에는 체력(Game_PlayerHpBroadcast)만 보낸다 - 공격력/방어력은
        // 클라이언트가 레벨(Game_ExpGainBroadcast)로 같은 공식(StatGrowth)을 계산해 표시한다.
        private void ApplyLevelUp(PlayerInfo player, int previousLevel)
        {
            (int currentHp, int maxHp) = PlayerCombatStats.ApplyLevelUp(player);

            Log.LogInformation("레벨업 (PlayerId={PlayerId}) : Lv{PreviousLevel} -> Lv{Level}, 공격력 {AttackPower}, 방어력 {Defense}, 최대 체력 {MaxHp}", player.PlayerId, previousLevel, player.Level, player.AttackPower, player.Defense, maxHp);

            var broadcast = new S2CPlayerHpBroadcast { PlayerId = player.PlayerId, CurrentHp = currentHp, MaxHp = maxHp };
            SendToViewersOfPlayer(broadcast.PlayerId, OpCode.Game_PlayerHpBroadcast, broadcast.Encode());
        }

        // 사망한 플레이어를 ReviveDelay 뒤 가득 찬 체력으로 부활시킨다. 그 사이 접속이 끊겼으면(방에서 빠짐)
        // 아무 것도 하지 않는다 - 재접속하면 입장 처리에서 어차피 가득 찬 체력으로 시작한다.
        // 사망 중에는 맵 이동이 막혀 있으므로(ClientSession.HandleMapChangeRequest) 다른 방으로 옮겨 갔을 일은 없다.
        private async Task ReviveAfterDelayAsync(PlayerInfo player)
        {
            try
            {
                await Task.Delay(ReviveDelay, _serverLifetimeCt);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!_players.TryGetValue(player.PlayerId, out var entry) || !ReferenceEquals(entry.Info, player))
            {
                return;
            }

            int currentHp;
            int maxHp;
            lock (player)
            {
                if (player.CurrentHp > 0)
                {
                    return;
                }

                player.CurrentHp = player.MaxHp;
                currentHp = player.CurrentHp;
                maxHp = player.MaxHp;

                // 부활 위치는 서버가 맵 데이터로 정한다(클라이언트가 옮긴 좌표를 받아주면 속도 검증을 우회하는 순간이동이 된다).
                // 맵 데이터가 없으면 쓰러진 자리에서 부활한다. 사망 중에는 이동 요청이 거부되므로 여기서 위치를 바꿔도 경합이 없다.
                if (MapDataCatalog.TryGet(_mapId, out MapData mapData) && mapData.RespawnPoint is { } respawnPoint)
                {
                    player.X = respawnPoint.X;
                    player.Y = respawnPoint.Y;
                    player.Z = respawnPoint.Z;
                    player.RotationY = respawnPoint.RotationY;
                }
            }

            var revived = new S2CPlayerRevived
            {
                PlayerId = player.PlayerId,
                CurrentHp = currentHp,
                MaxHp = maxHp,
                X = player.X,
                Y = player.Y,
                Z = player.Z,
                RotationY = player.RotationY
            };
            SendToViewersOfPlayer(player.PlayerId, OpCode.Game_PlayerRevived, revived.Encode());
        }
        #endregion
    }
}
