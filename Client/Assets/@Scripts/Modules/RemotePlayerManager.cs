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
    /// 이동(Game_MoveBroadcast) 이벤트를 받아 BasicCharacter 프리팹을 스폰/제거/갱신한다.
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
            GameServerConnectManager.Instance.OnPlayerMoved += HandlePlayerMoved;
            GameServerConnectManager.Instance.OnDamageReceived += HandlePlayerDamaged;
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
            GameServerConnectManager.Instance.OnPlayerLeft -= HandlePlayerLeft;
            GameServerConnectManager.Instance.OnPlayerMoved -= HandlePlayerMoved;
            GameServerConnectManager.Instance.OnDamageReceived -= HandlePlayerDamaged;
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
                    playerModel.ApplyRemoteCombatState(info.MaxHp, info.CurrentHp, info.AttackPower, info.Defense);
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

        private void HandlePlayerMoved(GameMoveBroadcastPacket move)
        {
            if (!remotePlayers.TryGetValue(move.PlayerId, out RemoteCharacterController controller) || controller == null)
            {
                return;
            }

            controller.SetTarget(new Vector3(move.X, move.Y, move.Z), move.RotationY);
        }

        /// <summary>
        /// Game_DamageBroadcast는 전원에게 오지만, 여기서는 TargetId가 원격 플레이어인 경우만 처리한다
        /// (로컬 플레이어가 맞은 경우는 GameSceneManager가 별도로 처리). Damage는 방어력 적용 전 원본값이라
        /// PlayerCharacterModel.TakeDamage가 이 클라이언트가 들고 있는 target의 로컬 Defense로 직접 계산한다.
        /// </summary>
        private void HandlePlayerDamaged(GameDamageBroadcastPacket packet)
        {
            if (!remotePlayers.TryGetValue(packet.TargetId, out RemoteCharacterController controller) || controller == null)
            {
                return;
            }

            if (controller.TryGetComponent(out PlayerCharacterModel playerModel))
            {
                playerModel.TakeDamage(new DamageInfo(packet.AttackerId, packet.Damage));
            }
        }
        #endregion
    }
}
