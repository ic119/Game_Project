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
        private readonly Queue<GameMonsterInfo> pendingSpawnQueue = new();
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
            GameServerConnectManager.Instance.OnMonsterMoved += HandleMonsterMoved;
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
            GameServerConnectManager.Instance.OnMonsterMoved -= HandleMonsterMoved;
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
                if (this == null)
                {
                    return;
                }

                if (result.Status != AsyncOperationStatus.Succeeded || result.Result == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"MonsterDatabaseSO 로드 실패(Status : {result.Status})");
                    return;
                }

                database = result.Result;

                // DB 로드가 끝나기 전에 도착해 대기 중이던 스폰 요청(주로 Game_EnterAck의 ExistingMonsters)을 그제서야 처리한다.
                while (pendingSpawnQueue.Count > 0)
                {
                    SpawnMonster(pendingSpawnQueue.Dequeue());
                }
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
                // DB 로드(비동기)가 아직 안 끝난 상태 - 유실시키지 않고 큐에 쌓아뒀다가 로드 완료 시 재처리한다.
                pendingSpawnQueue.Enqueue(info);
                return;
            }

            SpawnMonster(info);
        }

        /// <summary>
        /// database가 준비된 이후의 실제 스폰 처리. HandleMonsterSpawned(최초 도착 시)와 LoadDatabase의
        /// Completed 콜백(대기 큐 재처리 시) 양쪽에서 호출된다.
        /// </summary>
        private void SpawnMonster(GameMonsterInfo info)
        {
            if (remoteMonsters.ContainsKey(info.MonsterId))
            {
                return;
            }

            if (!database.TryGetByType(info.MonsterType, out MonsterData monsterData))
            {
                DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"MonsterDatabaseSO에 monsterType '{info.MonsterType}'에 대한 설정이 없습니다.");
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(monsterData.monsterType.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"몬스터 로드 실패 Key : {monsterData.monsterType}");
                    return;
                }

                // 로딩 중 이미 스폰되었거나(중복 이벤트) 사망 처리된 경우 대비.
                if (remoteMonsters.ContainsKey(info.MonsterId))
                {
                    return;
                }

                // 부모는 씬의 포인트 오브젝트로 두되(계층 정리용), 실제 배치 좌표는 항상 서버가 권위를 갖는
                // info.X/Y/Z를 그대로 쓴다. 같은 포인트에서 여러 마리가 스폰될 때 서버가 개체마다 흩뿌린
                // 좌표(GameRoom.SpawnMonsterAtPoint의 지터)를 여기서 포인트 위치로 덮어쓰면 다시 한 점에
                // 겹쳐버리기 때문이다.
                Transform spawnParent = FindSpawnPointTransform(info.PointId);
                if (spawnParent == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"PointId '{info.PointId}'에 해당하는 스폰 포인트 오브젝트를 GameScene에서 찾을 수 없어 기본 위치에 생성합니다.");
                    spawnParent = transform;
                }

                GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, spawnParent);
                instance.name = $"Monster_{info.MonsterType}_{info.MonsterId}";

                RemoteMonsterController controller = instance.AddComponent<RemoteMonsterController>();
                controller.Initialize(info.MonsterId, info.MonsterType, monsterData.displayName, monsterData.grade, info.ExpReward, info.MaxHp, info.CurrentHp);
                controller.Warp(new Vector3(info.X, info.Y, info.Z), info.RotationY);

                remoteMonsters[info.MonsterId] = controller;
            });
        }

        /// <summary>
        /// pointId와 이름이 같은 MonsterSpawnPointMarker 오브젝트를 현재 씬에서 찾는다. 맵 프리팹 안의 마커는
        /// MonsterSpawnPointExporter가 내보낼 때 이름을 그대로 pointId로 썼으므로(RemoteMonsterManager.cs 주석 참고)
        /// 이름 일치로 원본 포인트를 역으로 찾을 수 있다. 못 찾으면 null(호출부에서 기본 위치로 대체).
        /// </summary>
        private static Transform FindSpawnPointTransform(string pointId)
        {
            if (string.IsNullOrEmpty(pointId))
            {
                return null;
            }

            foreach (MonsterSpawnPointMarker marker in FindObjectsByType<MonsterSpawnPointMarker>(FindObjectsSortMode.None))
            {
                if (marker.gameObject.name == pointId)
                {
                    return marker.transform;
                }
            }

            return null;
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
        /// CurrentHp를 갱신(OnHpChanged 발생)하고 피격 트리거를 재생한다(HP 자체는 서버 권위값이라
        /// 별도 로컬 계산 없이 그대로 신뢰한다).
        /// </summary>
        private void HandleMonsterDamaged(GameMonsterDamageBroadcastPacket packet)
        {
            if (!remoteMonsters.TryGetValue(packet.MonsterId, out RemoteMonsterController controller) || controller == null)
            {
                return;
            }

            controller.ApplyDamage(packet.RemainingHp);
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

        /// <summary>
        /// Game_MonsterMoveBroadcast는 GameRoom의 AI 틱(추적/복귀)이 몬스터 위치를 바꿀 때마다 온다.
        /// RemoteCharacterController(원격 플레이어)와 동일하게 목표 위치/회전만 갱신하고, 실제 이동은
        /// RemoteMonsterController.Update()에서 매 프레임 보간한다.
        /// </summary>
        private void HandleMonsterMoved(GameMonsterMoveBroadcastPacket packet)
        {
            if (!remoteMonsters.TryGetValue(packet.MonsterId, out RemoteMonsterController controller) || controller == null)
            {
                return;
            }

            controller.SetTarget(new Vector3(packet.X, packet.Y, packet.Z), packet.RotationY);
        }
        #endregion
    }
}
