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
                }

                RemoteCharacterController controller = instance.AddComponent<RemoteCharacterController>();
                controller.Warp(new Vector3(info.X, info.Y, info.Z), info.RotationY);

                remotePlayers[info.PlayerId] = controller;
            });
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
        #endregion
    }
}
