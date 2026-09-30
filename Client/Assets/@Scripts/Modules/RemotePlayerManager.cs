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

        // 플레이어별 최신 장착 장비(itemId). 캐릭터 프리팹을 불러오는 동안(스폰 전)에 장비 변경 알림이 오면 스폰된 캐릭터가 없어
        // 바로 적용할 수 없으므로, 여기에 최신값을 쌓아 두었다가 스폰 직후 적용한다.
        private readonly Dictionary<long, (string Weapon, string Armor, string Helmet)> latestEquipment = new();

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
            GameServerConnectManager.Instance.OnEquipmentChanged += HandleEquipmentChanged;

            // ItemDatabaseSO는 Addressables로 비동기 로드되므로 원격 캐릭터가 데이터베이스 로드 전에 스폰될 수 있다 -
            // 로드가 끝나면 그때까지 못 그린 장비를 다시 적용한다.
            if (ItemDatabaseManager.Instance != null)
            {
                ItemDatabaseManager.Instance.OnDatabaseLoaded += HandleItemDatabaseLoaded;
            }
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
            GameServerConnectManager.Instance.OnEquipmentChanged -= HandleEquipmentChanged;

            if (ItemDatabaseManager.Instance != null)
            {
                ItemDatabaseManager.Instance.OnDatabaseLoaded -= HandleItemDatabaseLoaded;
            }
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

            // 프리팹 로딩 중에 오는 장비 변경 알림이 더 최신이므로, 이미 기록된 값이 있으면 덮어쓰지 않는다.
            if (!latestEquipment.ContainsKey(info.PlayerId))
            {
                latestEquipment[info.PlayerId] = (info.WeaponItemId, info.ArmorItemId, info.HelmetItemId);
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
                ApplyLatestEquipment(info.PlayerId);
            });
        }

        /// <summary>
        /// 다른 플레이어의 장착 장비가 바뀌었다(Game_EquipmentChangedBroadcast). 이 플레이어가 아직 스폰 전이어도 최신값은 기록해 둔다.
        /// 본인의 id면 remotePlayers에 없으므로 자연히 적용되지 않는다(본인 외형은 로컬 장착 흐름이 이미 반영했다).
        /// </summary>
        private void HandleEquipmentChanged(GameEquipmentChangedPacket packet)
        {
            latestEquipment[packet.PlayerId] = (packet.WeaponItemId, packet.ArmorItemId, packet.HelmetItemId);
            ApplyLatestEquipment(packet.PlayerId);
        }

        private void HandleItemDatabaseLoaded()
        {
            foreach (long playerId in remotePlayers.Keys)
            {
                ApplyLatestEquipment(playerId);
            }
        }

        /// <summary>
        /// 기록된 최신 장비를 스폰된 원격 캐릭터에 그린다. 빈 슬롯(또는 클라이언트 데이터베이스에 없는 itemId)은 해제로 취급한다 -
        /// 무기는 기본 무기, 갑옷은 기본 몸, 투구는 맨머리로 돌아간다. 데이터베이스가 아직 로드되지 않았으면 건너뛰고,
        /// 로드가 끝난 뒤 HandleItemDatabaseLoaded가 다시 적용한다.
        /// </summary>
        private void ApplyLatestEquipment(long playerId)
        {
            if (!latestEquipment.TryGetValue(playerId, out var equipment)
                || !remotePlayers.TryGetValue(playerId, out RemoteCharacterController controller) || controller == null
                || ItemDatabaseManager.Instance == null || !ItemDatabaseManager.Instance.IsLoaded
                || !controller.TryGetComponent(out PlayerCharacterModel model))
            {
                return;
            }

            ApplySlot(model, EquipmentSlotType.Weapon, equipment.Weapon);
            ApplySlot(model, EquipmentSlotType.Armor, equipment.Armor);
            ApplySlot(model, EquipmentSlotType.Helmet, equipment.Helmet);
        }

        private static void ApplySlot(PlayerCharacterModel model, EquipmentSlotType slot, string itemId)
        {
            ItemData itemData = string.IsNullOrEmpty(itemId) ? null : ItemDatabaseManager.Instance.FindById(itemId);
            if (itemData != null && itemData.equipSlotType == slot)
            {
                model.EquipItem(itemData);
            }
            else
            {
                model.UnequipItem(slot);
            }
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
            latestEquipment.Clear();
        }

        private void HandlePlayerLeft(long playerId)
        {
            latestEquipment.Remove(playerId);

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
                    controller.AddSnapshot(snapshot.ServerTimeMs, new Vector3(player.X, player.Y, player.Z), player.RotationY);
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
