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

        // 아직 생성 전(DB 로드 대기열에 있거나 프리팹을 불러오는 중)에 시야 이탈이 온 몬스터 id. 생성할 차례에 한 번 건너뛴다 -
        // 그렇지 않으면 이미 시야 밖으로 나간 몬스터가 뒤늦게 생성돼 아무 갱신도 받지 않은 채 남는다.
        private readonly HashSet<long> leftViewWhilePending = new();

        // 프리팹을 불러오는 중(SpawnMonster 호출 후 콜백 전)인 몬스터 id.
        private readonly HashSet<long> loadingMonsterIds = new();

        // 보스 스킬 위험 범위 표시. 몬스터 id -> 지금 보이는 표시들.
        private readonly Dictionary<long, List<BossTelegraphIndicator>> bossIndicators = new();

        // 몬스터 id -> 예고/종료/정리 때마다 올리는 번호. 표시를 풀에서 비동기로 빌리는 동안 종료 알림이 먼저 오면, 뒤늦게 만들어진 표시가
        // 이미 끝난 예고를 화면에 남기지 않도록 번호가 달라진 표시는 버린다.
        private readonly Dictionary<long, int> bossSkillVersions = new();

        // 소환 위치 표시(작은 원)의 반지름(m). 하수인 몸집이 들어갈 만한 크기다.
        private const float SummonMarkerRadius = 1.0f;

        // pointId(마커 오브젝트 이름) -> 마커 Transform. FindSpawnPointTransform 참고.
        private readonly Dictionary<string, Transform> spawnPointCache = new();
        private MonsterDatabaseSO database;

        #region LifeCycle
        protected override void Awake()
        {
            base.Awake();
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
            GameServerConnectManager.Instance.OnWorldSnapshot += HandleWorldSnapshot;
            GameServerConnectManager.Instance.OnMonsterLeftView += HandleMonsterLeftView;
            GameServerConnectManager.Instance.OnMonsterAttackStarted += HandleMonsterAttackStarted;
            GameServerConnectManager.Instance.OnBossSkillTelegraph += HandleBossSkillTelegraph;
            GameServerConnectManager.Instance.OnBossSkillEnd += HandleBossSkillEnd;
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
            GameServerConnectManager.Instance.OnWorldSnapshot -= HandleWorldSnapshot;
            GameServerConnectManager.Instance.OnMonsterLeftView -= HandleMonsterLeftView;
            GameServerConnectManager.Instance.OnMonsterAttackStarted -= HandleMonsterAttackStarted;
            GameServerConnectManager.Instance.OnBossSkillTelegraph -= HandleBossSkillTelegraph;
            GameServerConnectManager.Instance.OnBossSkillEnd -= HandleBossSkillEnd;
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
            if (remoteMonsters.ContainsKey(info.MonsterId) || leftViewWhilePending.Remove(info.MonsterId))
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

            loadingMonsterIds.Add(info.MonsterId);
            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(monsterData.monsterType.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                loadingMonsterIds.Remove(info.MonsterId);

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<RemoteMonsterManager>($"몬스터 로드 실패 Key : {monsterData.monsterType}");
                    return;
                }

                // 로딩 중 이미 스폰되었거나(중복 이벤트) 사망 처리된 경우, 또는 로딩 중 시야 밖으로 나간 경우 대비.
                if (remoteMonsters.ContainsKey(info.MonsterId) || leftViewWhilePending.Remove(info.MonsterId))
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
        private Transform FindSpawnPointTransform(string pointId)
        {
            if (string.IsNullOrEmpty(pointId))
            {
                return null;
            }

            // 몬스터가 스폰될 때마다 씬 전체를 검색하지 않도록 이름으로 캐시해 둔다. 캐시된 마커가 파괴됐거나(맵 전환)
            // 아직 모르는 pointId면 그때만 다시 모은다.
            if (spawnPointCache.TryGetValue(pointId, out Transform cached) && cached != null)
            {
                return cached;
            }

            RebuildSpawnPointCache();
            return spawnPointCache.TryGetValue(pointId, out cached) ? cached : null;
        }

        private void RebuildSpawnPointCache()
        {
            spawnPointCache.Clear();

            foreach (MonsterSpawnPointMarker marker in FindObjectsByType<MonsterSpawnPointMarker>())
            {
                spawnPointCache.TryAdd(marker.gameObject.name, marker.transform);
            }
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

            // 이전 맵 보스의 위험 범위 표시도 함께 걷는다.
            foreach (long monsterId in new List<long>(bossIndicators.Keys))
            {
                ReleaseBossVisuals(monsterId);
            }

            bossSkillVersions.Clear();

            // 이전 맵의 마커는 곧 사라지고 새 맵의 마커로 바뀌므로, 같은 이름의 마커를 가리키는 낡은 캐시를 버린다.
            spawnPointCache.Clear();
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
        /// Game_MonsterAttackStartBroadcast는 몬스터가 근접 사거리 안의 플레이어를 공격하기 시작할 때마다 온다(선딜 시작).
        /// 여기서는 공격 애니메이션만 재생한다 - 피해는 선딜이 끝난 뒤의 판정 결과(Game_MonsterAttackBroadcast, 대상이
        /// 로컬 플레이어면 GameSceneManager.HandleMonsterAttackReceived)로 따로 온다. 그 사이 플레이어는 대쉬로 피할 수 있다.
        /// </summary>
        private void HandleMonsterAttackStarted(GameMonsterAttackStartBroadcastPacket packet)
        {
            if (!remoteMonsters.TryGetValue(packet.MonsterId, out RemoteMonsterController controller) || controller == null)
            {
                return;
            }

            controller.PlayAttack();
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
            ReleaseBossVisuals(packet.MonsterId);
            controller.PlayDeath();
            Destroy(controller.gameObject, dieAnimationDuration);
        }

        /// <summary>
        /// 보스가 스킬 예고를 시작했다(Game_BossSkillTelegraphBroadcast). 바닥에 위험 범위를 DurationMs 동안 차오르게 보여주고 시전 모션을
        /// 재생한다. 위험 범위는 서버가 판정할 범위와 같은 크기/위치다 - 플레이어는 예고가 끝나기 전에 범위 밖으로 벗어나거나 대쉬로 피한다.
        /// </summary>
        private void HandleBossSkillTelegraph(GameBossSkillTelegraphBroadcastPacket packet)
        {
            var skillType = (BossSkillType)packet.SkillType;
            long monsterId = packet.MonsterId;

            // 이전 예고가 남아 있으면(종료 알림 유실 등) 먼저 걷고 새 번호로 시작한다.
            ReleaseBossVisuals(monsterId);
            int version = bossSkillVersions[monsterId];

            float groundReferenceY = 0f;
            if (remoteMonsters.TryGetValue(monsterId, out RemoteMonsterController controller) && controller != null)
            {
                controller.PlayBossSkillTelegraph(skillType);
                groundReferenceY = controller.transform.position.y;
            }

            float durationSeconds = packet.DurationMs / 1000f;
            var center = new Vector3(packet.CenterX, groundReferenceY, packet.CenterZ);

            switch (skillType)
            {
                case BossSkillType.AreaSlam:
                    SpawnBossIndicator(monsterId, version, indicator => indicator.SetupCircle(center, packet.Radius, durationSeconds));
                    break;

                case BossSkillType.Charge:
                    SpawnBossIndicator(monsterId, version, indicator => indicator.SetupLine(center, packet.RotationY, packet.Width, packet.Length, durationSeconds));
                    break;

                case BossSkillType.Summon:
                    foreach (GameBossSkillPoint point in packet.Points)
                    {
                        var position = new Vector3(point.X, groundReferenceY, point.Z);
                        SpawnBossIndicator(monsterId, version, indicator => indicator.SetupMarker(position, SummonMarkerRadius, durationSeconds));
                    }
                    break;
            }
        }

        /// <summary>
        /// 보스 스킬의 예고가 끝났다(Game_BossSkillEndBroadcast). 발동이면 표시가 섬광을 내고 사라지며 보스가 타격/돌진 모션을 재생하고,
        /// 취소(보스 사망 등)면 표시만 걷는다. 피해/회피 결과는 이 알림과 별개로 Game_MonsterAttackBroadcast/AttackDodgedBroadcast로 온다.
        /// </summary>
        private void HandleBossSkillEnd(GameBossSkillEndBroadcastPacket packet)
        {
            bossSkillVersions[packet.MonsterId] = bossSkillVersions.GetValueOrDefault(packet.MonsterId) + 1;

            if (bossIndicators.Remove(packet.MonsterId, out List<BossTelegraphIndicator> indicators))
            {
                foreach (BossTelegraphIndicator indicator in indicators)
                {
                    if (indicator != null)
                    {
                        indicator.Complete(packet.Executed);
                    }
                }
            }

            if (packet.Executed && remoteMonsters.TryGetValue(packet.MonsterId, out RemoteMonsterController controller) && controller != null)
            {
                controller.PlayBossSkillExecute((BossSkillType)packet.SkillType);
            }
        }

        /// <summary>
        /// 풀에서 위험 범위 표시 하나를 빌려 setup으로 모양을 정한다. 빌리는 동안(비동기) 종료 알림이 먼저 와서 번호가 바뀌었다면 표시를 바로 돌려준다.
        /// </summary>
        private void SpawnBossIndicator(long monsterId, int version, Action<BossTelegraphIndicator> setup)
        {
            if (ObjectPoolManager.Instance == null)
            {
                return;
            }

            ObjectPoolManager.Instance.GetAsync(AddressableAssetKey.BossTelegraph01.ToString(), spawned =>
            {
                if (spawned == null)
                {
                    return;
                }

                bool stillCurrent = this != null
                    && bossSkillVersions.TryGetValue(monsterId, out int current) && current == version;

                if (!stillCurrent || !spawned.TryGetComponent(out BossTelegraphIndicator indicator))
                {
                    ObjectPoolManager.Instance?.Release(spawned);
                    return;
                }

                setup(indicator);

                if (!bossIndicators.TryGetValue(monsterId, out List<BossTelegraphIndicator> list))
                {
                    list = new List<BossTelegraphIndicator>();
                    bossIndicators[monsterId] = list;
                }

                list.Add(indicator);
            });
        }

        /// <summary>
        /// 이 몬스터의 위험 범위 표시를 모두 걷고(페이드) 번호를 올려 아직 빌리는 중인 표시도 무효로 만든다. 보스 사망/시야 이탈/맵 전환/
        /// 새 예고 시작 때 쓴다.
        /// </summary>
        private void ReleaseBossVisuals(long monsterId)
        {
            bossSkillVersions[monsterId] = bossSkillVersions.GetValueOrDefault(monsterId) + 1;

            if (!bossIndicators.Remove(monsterId, out List<BossTelegraphIndicator> indicators))
            {
                return;
            }

            foreach (BossTelegraphIndicator indicator in indicators)
            {
                if (indicator != null)
                {
                    indicator.Complete(false);
                }
            }
        }

        /// <summary>
        /// 서버 방 틱(20Hz)마다 오는 스냅샷(Game_WorldSnapshot)에서 이번 틱에 움직인 몬스터(추적/복귀) 위치만 반영한다.
        /// RemoteCharacterController(원격 플레이어)와 동일하게 목표 위치/회전만 갱신하고, 실제 이동은
        /// RemoteMonsterController.Update()에서 매 프레임 보간한다.
        /// </summary>
        /// <summary>
        /// 몬스터가 내 관심 영역 밖으로 나갔다(Game_MonsterLeaveView). 죽은 게 아니므로 사망 연출 없이 바로 제거한다.
        /// 다시 시야에 들어오면 서버가 Game_MonsterSpawnBroadcast로 현재 상태(위치/HP)를 다시 보내 새로 생성된다.
        /// </summary>
        private void HandleMonsterLeftView(long monsterId)
        {
            if (!remoteMonsters.TryGetValue(monsterId, out RemoteMonsterController controller))
            {
                // 아직 생성 전(DB 로드 대기열 또는 프리팹 로딩 중)인 경우 - 나중에 생성하지 않도록 표시해 둔다.
                if (database == null || loadingMonsterIds.Contains(monsterId))
                {
                    leftViewWhilePending.Add(monsterId);
                }
                return;
            }

            remoteMonsters.Remove(monsterId);
            ReleaseBossVisuals(monsterId);
            if (controller != null)
            {
                Destroy(controller.gameObject);
            }
        }

        private void HandleWorldSnapshot(GameWorldSnapshotPacket snapshot)
        {
            foreach (GameEntityTransform monster in snapshot.Monsters)
            {
                if (remoteMonsters.TryGetValue(monster.Id, out RemoteMonsterController controller) && controller != null)
                {
                    controller.AddSnapshot(snapshot.ServerTimeMs, new Vector3(monster.X, monster.Y, monster.Z), monster.RotationY);
                }
            }
        }
        #endregion
    }
}
