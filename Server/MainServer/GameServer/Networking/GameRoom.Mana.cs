using GameServer.Combat;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 마나: 소모(스킬 사용)와 자연 회복, 본인에게 알리기. 마나는 체력과 같은 구조(DB에 저장하지 않는 서버 메모리 값, 입장/레벨업/부활 때 가득 참)이고,
    // 다른 점은 (1) 자연 회복이 있고(체력은 물약/레벨업/부활로만 찬다) (2) 본인에게만 알린다는 것이다.
    public partial class GameRoom
    {
        #region Method - Mana
        // 마나 변경 알림을 너무 자주 보내지 않기 위한 최소 간격. 자연 회복은 매 틱(50ms) 조금씩 차지만 화면에 필요한 건 초당 한 번 정도의 갱신이다.
        // 가득 찼을 때와 소모/충전처럼 즉시 반영돼야 하는 변화는 이 간격과 무관하게 바로 보낸다.
        private static readonly TimeSpan ManaRegenNotifyInterval = TimeSpan.FromSeconds(1);

        // 마나를 amount만큼 쓴다. 죽었거나, 마나가 모자라거나, amount가 0 이하면 false(마나는 그대로). 성공하면 본인에게 바뀐 마나를 알린다.
        // 스킬 사용 요청이 이 메서드로 마나를 먼저 차감해야 한다 - 서버가 권위로 정하므로 클라이언트가 마나가 있다고 주장해도 소용없다.
        public bool TrySpendMana(long playerId, int amount)
        {
            if (amount <= 0 || !_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            PlayerInfo player = entry.Info;
            lock (player)
            {
                if (player.CurrentHp <= 0 || player.CurrentMp < amount)
                {
                    return false;
                }

                player.CurrentMp -= amount;

                // 소모 직후에는 소수점 회복분도 비워 둔다 - 다음 회복이 새로 쌓이게 해 "소모와 동시에 한 칸 차는" 현상을 막는다.
                player.ManaRegenRemainder = 0f;
            }

            SendManaUpdate(player, force: true);
            return true;
        }

        // 방 틱마다 호출된다(RunTickLoopAsync). 살아 있고 마나가 가득 차지 않은 플레이어의 마나를 deltaSeconds만큼 회복한다.
        // 회복량은 최대 마나의 CombatTuning.ManaRegenPercentPerSecond%라 지능/레벨이 높을수록 절대량도 늘어난다. 소수점 회복분은
        // 플레이어별로 모았다가(ManaRegenRemainder) 1 이상이 되면 정수로 더한다 - 틱당 회복량이 1보다 작아도 손실이 없다.
        public void RegenerateMana(float deltaSeconds)
        {
            double percentPerSecond = CombatTuning.Current.ManaRegenPercentPerSecond;
            if (percentPerSecond <= 0 || deltaSeconds <= 0f)
            {
                return;
            }

            foreach (var (_, entry) in _players)
            {
                PlayerInfo player = entry.Info;
                bool changed = false;

                lock (player)
                {
                    if (player.CurrentHp <= 0 || player.MaxMp <= 0 || player.CurrentMp >= player.MaxMp)
                    {
                        continue;
                    }

                    player.ManaRegenRemainder += CombatStatCalculator.CalculateManaRegenPerSecond(player.MaxMp, percentPerSecond) * deltaSeconds;
                    int whole = (int)player.ManaRegenRemainder;
                    if (whole <= 0)
                    {
                        continue;
                    }

                    player.ManaRegenRemainder -= whole;
                    player.CurrentMp = Math.Min(player.MaxMp, player.CurrentMp + whole);

                    if (player.CurrentMp >= player.MaxMp)
                    {
                        player.ManaRegenRemainder = 0f;
                    }

                    changed = true;
                }

                if (changed)
                {
                    SendManaUpdate(player, force: false);
                }
            }
        }

        // 본인에게 현재 마나를 알린다. force가 false면(자연 회복) 마지막 알림 후 ManaRegenNotifyInterval이 지나야 보내되, 가득 찼으면 바로 보내
        // 화면이 가득 찬 상태에서 멈추지 않게 한다. 레벨업/부활/소모처럼 즉시 반영돼야 하는 변화는 force=true로 호출한다.
        private void SendManaUpdate(PlayerInfo player, bool force)
        {
            int currentMp;
            int maxMp;
            long now = DateTime.UtcNow.Ticks;

            lock (player)
            {
                currentMp = player.CurrentMp;
                maxMp = player.MaxMp;

                bool isFull = currentMp >= maxMp;
                if (!force && !isFull && now - player.LastManaSentAtUtcTicks < ManaRegenNotifyInterval.Ticks)
                {
                    return;
                }

                player.LastManaSentAtUtcTicks = now;
            }

            if (!_players.TryGetValue(player.PlayerId, out var entry) || !ReferenceEquals(entry.Info, player))
            {
                return;
            }

            var update = new S2CPlayerMpUpdate { PlayerId = player.PlayerId, CurrentMp = currentMp, MaxMp = maxMp };
            entry.Session.Send(OpCode.Game_PlayerMpUpdate, update.Encode());
        }
        #endregion
    }
}
