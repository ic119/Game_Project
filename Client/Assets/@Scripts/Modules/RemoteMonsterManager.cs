using System;
using System.Collections.Generic;
using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Models.SO;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Modules
{
    /// <summary>
    /// GameServerConnectManager가 수신한 몬스터 입장/스폰(Game_EnterAck.ExistingMonsters, Game_MonsterSpawnBroadcast)/
    /// 피격(Game_MonsterDamageBroadcast)/사망(Game_MonsterDieBroadcast) 이벤트를 받아 몬스터 프리팹을 스폰/갱신/제거한다.
    /// RemotePlayerManager와 같은 패턴이다: GameScene 동안만 존재하면 되므로 PersistAcrossScenes를 쓰지 않는다.
    /// </summary>
    public class RemoteMonsterManager : SingletonObject<RemoteMonsterManager>
    {
        private const string DatabaseAddressableName = "MonsterDatabaseSO";

        [Tooltip("Die 트리거 재생 후 오브젝트를 실제로 제거하기까지 대기하는 시간(초). 죽는 애니메이션이 " +
            "끝까지 보이도록 여유를 둔다.")]
        [SerializeField, Min(0f)] private float dieAnimationDuration = 1.5f;

        private readonly Dictionary<long, RemoteMonsterController> remoteMonsters = new();
        private MonsterDatabaseSO database;

        #region LifeCycle
        private void Awake()
        {
            LoadDatabase();
        }

        private void OnEnable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnMonsterSpawned += HandleMonsterSpawned;
            GameServerConnectManager.Instance.OnMonsterDamaged += HandleMonsterDamaged;
            GameServerConnectManager.Instance.OnMonsterDied += HandleMonsterDied;
        }

        private void OnDisable()
        {
            if (GameServerConnectManager.Instance == null)
            {
                return;
            }

            GameServerConnectManager.Instance.OnMonsterSpawned -= HandleMonsterSpawned;
            GameServerConnectManager.Instance.OnMonsterDamaged -= HandleMonsterDamaged;
            GameServerConnectManager.Instance.OnMonsterDied -= HandleMonsterDied;
        }
        #endregion

        #region Method
        private void LoadDatabase()
        {
            AsyncOperationHandle<MonsterDatabaseSO> handle;

            try
            {
                handle = Addressables.LoadAssetAsync<MonsterDatabaseSO>(DatabaseAddressableName);
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"MonsterDatabaseSO 로드 실패(잘못된 Key) : {exception}");
                return;
            }

            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"MonsterDatabaseSO 로드 실패(Status : {result.Status})");
                    return;
                }

                database = result.Result;
            };
        }

        /// <summary>
        /// Game_EnterAck/Game_MapChangeAck(기존 몬스터 목록)/Game_MonsterSpawnBroadcast(리스폰) 전부 이 핸들러로 들어온다.
        /// </summary>
        private void HandleMonsterSpawned(GameMonsterInfo info)
        {
            if (remoteMonsters.ContainsKey(info.MonsterId))
            {
                return;
            }

            if (database == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>("MonsterDatabaseSO가 아직 로드되지 않았습니다.");
                return;
            }

            if (!database.TryGetByType(info.MonsterType, out MonsterData monsterData) || monsterData.addressableKey == AddressableAssetKey.None)
            {
                DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"MonsterDatabaseSO에 monsterType '{info.MonsterType}'에 대한 설정이 없습니다.");
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(monsterData.addressableKey.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"몬스터 로드 실패 Key : {monsterData.addressableKey}");
                    return;
                }

                // 로딩 중 이미 스폰되었거나(중복 이벤트) 사망 처리된 경우 대비.
                if (remoteMonsters.ContainsKey(info.MonsterId))
                {
                    return;
                }

                GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);
                instance.name = $"Monster_{info.MonsterType}_{info.MonsterId}";

                RemoteMonsterController controller = instance.AddComponent<RemoteMonsterController>();
                controller.SetMonsterId(info.MonsterId, info.MonsterType);
                controller.Warp(new Vector3(info.X, info.Y, info.Z), info.RotationY);

                remoteMonsters[info.MonsterId] = controller;
            });
        }

        /// <summary>
        /// monsterId에 해당하는 원격 몬스터를 조회한다. PlayerAttackController가 공격 대상 판정 및
        /// 자신의 공격이 명중한 대상 위치에 임팩트 이펙트를 재생할 때 사용한다.
        /// </summary>
        public bool TryGetRemoteMonster(long monsterId, out RemoteMonsterController controller)
        {
            return remoteMonsters.TryGetValue(monsterId, out controller) && controller != null;
        }

        /// <summary>
        /// 맵 전환 시작 시 호출한다. 지금 스폰되어 있는 몬스터는 전부 이전 맵 소속이므로,
        /// 서버의 Game_MapChangeAck(새 맵 기존 몬스터 목록)로 다시 채워지기 전에 미리 전부 비워야 한다.
        /// </summary>
        public void ClearAll()
        {
            foreach (RemoteMonsterController controller in remoteMonsters.Values)
            {
                if (controller != null)
                {
                    Destroy(controller.gameObject);
                }
            }

            remoteMonsters.Clear();
        }

        /// <summary>
        /// Game_MonsterDamageBroadcast는 전원에게 온다. 대상 몬스터가 아직 이 클라이언트에 스폰돼 있으면
        /// 피격 트리거만 재생한다(HP 자체는 서버 권위값이라 별도 로컬 계산 없이 그대로 신뢰한다).
        /// </summary>
        private void HandleMonsterDamaged(GameMonsterDamageBroadcastPacket packet)
        {
            if (!remoteMonsters.TryGetValue(packet.MonsterId, out RemoteMonsterController controller) || controller == null)
            {
                return;
            }

            controller.PlayHitReaction();
        }

        /// <summary>
        /// 사망 트리거를 재생하고, 애니메이션이 보일 시간(dieAnimationDuration)만큼 기다린 뒤 제거한다.
        /// 리스폰된 새 개체는 별도의 MonsterId로 Game_MonsterSpawnBroadcast를 통해 다시 들어온다.
        /// </summary>
        private void HandleMonsterDied(GameMonsterDieBroadcastPacket packet)
        {
            if (!remoteMonsters.TryGetValue(packet.MonsterId, out RemoteMonsterController controller) || controller == null)
            {
                return;
            }

            remoteMonsters.Remove(packet.MonsterId);
            controller.PlayDeath();
            Destroy(controller.gameObject, dieAnimationDuration);
        }
        #endregion
    }
}
