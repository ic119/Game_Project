using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules.Networking;
using Incheol.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// GameServerConnectManager가 수신한 다른 플레이어의 입장(Game_PlayerJoined)/퇴장(Game_PlayerLeft)/
    /// 이동(Game_WorldSnapshot) 이벤트를 받아 BasicCharacter 프리팹을 스폰/제거/갱신한다.
    /// GameServerConnectManager와 달리 GameScene 동안만 존재하면 되므로 PersistAcrossScenes를 쓰지 않는다 -
    /// 씬이 언로드되면 이 매니저와 그 자식으로 스폰된 원격 캐릭터들도 함께 파괴된다.
    /// </summary>
    public class RemotePlayerManager : SingletonObject<RemotePlayerManager>
    {
        private readonly Dictionary<long, RemoteCharacterController> remotePlayers = new();

        #region LifeCycle
        private void OnEnable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnPlayerJoined += HandlePlayerJoined;
            GameServerConnectManager.Instance.OnPlayerLeft += HandlePlayerLeft;
            GameServerConnectManager.Instance.OnWorldSnapshot += HandleWorldSnapshot;
            GameServerConnectManager.Instance.OnDamageReceived += HandlePlayerDamaged;
            GameServerConnectManager.Instance.OnMonsterAttacked += HandleMonsterAttackedPlayer;
            GameServerConnectManager.Instance.OnPlayerHpChanged += HandlePlayerHpChanged;
            GameServerConnectManager.Instance.OnPlayerRevived += HandlePlayerRevived;
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
            GameServerConnectManager.Instance.OnPlayerLeft -= HandlePlayerLeft;
            GameServerConnectManager.Instance.OnWorldSnapshot -= HandleWorldSnapshot;
            GameServerConnectManager.Instance.OnDamageReceived -= HandlePlayerDamaged;
            GameServerConnectManager.Instance.OnMonsterAttacked -= HandleMonsterAttackedPlayer;
            GameServerConnectManager.Instance.OnPlayerHpChanged -= HandlePlayerHpChanged;
            GameServerConnectManager.Instance.OnPlayerRevived -= HandlePlayerRevived;
        }
        #endregion

        #region Method
        /// <summary>
        /// Game_EnterAck(기존 접속자 목록)/Game_PlayerJoined 둘 다 이 핸들러로 들어온다.
        /// BasicCharacter를 Addressable로 로드해 world 좌표(info.X/Y/Z)에 즉시 배치(Warp)한다.
        /// </summary>
        private void HandlePlayerJoined(GamePlayerInfo info)
        {
            if (remotePlayers.ContainsKey(info.PlayerId))
            {
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<RemotePlayerManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.BasicCharacter.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemotePlayerManager>($"원격 플레이어 캐릭터 로드 실패 Key : {AddressableAssetKey.BasicCharacter}");
                    return;
                }

                // 로딩 중 같은 플레이어가 이미 스폰되었거나(중복 이벤트) 퇴장한 경우 대비.
                if (remotePlayers.ContainsKey(info.PlayerId))
                {
                    return;
                }

                GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                instance.name = $"RemotePlayer_{info.PlayerId}";

                if (instance.TryGetComponent(out CharacterCustomModel customModel))
                {
                    customModel.ApplyCustomization(info.HairIndex, info.EyeIndex, info.MouthIndex);
                }

                if (instance.TryGetComponent(out PlayerCharacterModel playerModel))
                {
                    playerModel.SetNickname(info.Nickname);
                    // 체력 0으로 들어왔으면(사망한 채 부활 대기 중) ApplyServerHp로 한 번 더 반영해 쓰러진 모습으로 시작한다.
                    playerModel.ApplyRemoteCombatState(info.MaxHp, info.MaxHp, info.AttackPower, info.Defense);
                    playerModel.ApplyServerHp(info.CurrentHp, info.MaxHp, false);
                }

                RemoteCharacterController controller = instance.AddComponent<RemoteCharacterController>();
                controller.SetPlayerId(info.PlayerId);
                controller.Warp(new Vector3(info.X, info.Y, info.Z), info.RotationY);

                remotePlayers[info.PlayerId] = controller;
            });
        }

        /// <summary>
        /// playerId에 해당하는 원격 플레이어를 조회한다. PlayerAttackController가 자신의 공격이 명중한
        /// 대상의 위치에 임팩트 이펙트를 재생할 때 사용한다.
        /// </summary>
        public bool TryGetRemotePlayer(long playerId, out RemoteCharacterController controller)
        {
            return remotePlayers.TryGetValue(playerId, out controller) && controller != null;
        }

        /// <summary>
        /// 맵 전환 시작 시 호출한다. 지금 스폰되어 있는 원격 플레이어는 전부 이전 맵 소속이므로,
        /// 서버의 Game_MapChangeAck(새 맵 기존 접속자 목록)로 다시 채워지기 전에 미리 전부 비워야 한다.
        /// </summary>
        public void ClearAll()
        {
            foreach (RemoteCharacterController controller in remotePlayers.Values)
            {
                if (controller != null)
                {
                    Destroy(controller.gameObject);
                }
            }

            remotePlayers.Clear();
        }

        private void HandlePlayerLeft(long playerId)
        {
            if (!remotePlayers.TryGetValue(playerId, out RemoteCharacterController controller))
            {
                return;
            }

            remotePlayers.Remove(playerId);

            if (controller != null)
            {
                Destroy(controller.gameObject);
            }
        }

        /// <summary>
        /// 서버 방 틱마다 오는 스냅샷에서 원격 플레이어 위치만 반영한다(몬스터는 RemoteMonsterManager가 같은 패킷을 따로 처리).
        /// 내 캐릭터 id는 remotePlayers에 없으므로 자연히 건너뛴다.
        /// </summary>
        private void HandleWorldSnapshot(GameWorldSnapshotPacket snapshot)
        {
            foreach (GameEntityTransform player in snapshot.Players)
            {
                if (remotePlayers.TryGetValue(player.Id, out RemoteCharacterController controller) && controller != null)
                {
                    controller.SetTarget(new Vector3(player.X, player.Y, player.Z), player.RotationY);
                }
            }
        }

        /// <summary>
        /// Game_DamageBroadcast는 전원에게 오지만, 여기서는 TargetId가 원격 플레이어인 경우만 처리한다
        /// (로컬 플레이어가 맞은 경우는 GameSceneManager가 별도로 처리). 서버가 계산한 RemainingHp를 그대로 반영한다.
        /// </summary>
        private void HandlePlayerDamaged(GameDamageBroadcastPacket packet)
        {
            ApplyRemoteServerHp(packet.TargetId, packet.RemainingHp, null, true);
        }

        /// <summary>
        /// 몬스터가 원격 플레이어를 공격한 경우의 체력 반영(몬스터 공격 애니메이션은 RemoteMonsterManager가 처리).
        /// </summary>
        private void HandleMonsterAttackedPlayer(GameMonsterAttackBroadcastPacket packet)
        {
            ApplyRemoteServerHp(packet.TargetPlayerId, packet.RemainingHp, null, true);
        }

        private void HandlePlayerHpChanged(GamePlayerHpBroadcastPacket packet)
        {
            ApplyRemoteServerHp(packet.PlayerId, packet.CurrentHp, packet.MaxHp, false);
        }

        // 부활 위치는 서버가 정해 패킷에 담아 보낸다 - 보간 없이 그 자리로 바로 옮긴다.
        private void HandlePlayerRevived(GamePlayerRevivedPacket packet)
        {
            if (remotePlayers.TryGetValue(packet.PlayerId, out RemoteCharacterController controller) && controller != null)
            {
                controller.Warp(new Vector3(packet.X, packet.Y, packet.Z), packet.RotationY);
            }

            ApplyRemoteServerHp(packet.PlayerId, packet.CurrentHp, packet.MaxHp, false);
        }

        // maxHp가 null이면(피격 패킷에는 최대 체력이 없음) 현재 알고 있는 최대 체력을 유지한다.
        // 로컬 플레이어 id면 remotePlayers에 없으므로 자연히 무시된다(GameSceneManager가 처리).
        private void ApplyRemoteServerHp(long playerId, int currentHp, int? maxHp, bool wasHit)
        {
            if (!remotePlayers.TryGetValue(playerId, out RemoteCharacterController controller) || controller == null)
            {
                return;
            }

            if (controller.TryGetComponent(out PlayerCharacterModel playerModel))
            {
                playerModel.ApplyServerHp(currentHp, maxHp ?? playerModel.MaxHp, wasHit);
            }
        }
        #endregion
    }
}
