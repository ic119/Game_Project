using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Presenter.Scene
{
    // 맵 이동(포털): 새 맵을 먼저 로드/검증한 뒤 서버 승인(Game_MapChangeAck)을 받고서야 교체한다. 필드는 GameSceneManager.cs에 있다.
    public partial class GameSceneManager
    {
        #region Method
        /// <summary>
        /// MapPortalController(PortalTeleportType.MapSwap)가 호출한다. Scene을 전환하지 않고 현재 맵 프리팹만
        /// 제거한 뒤 새 맵을 로드해서, 그 안의 _entryPointName Transform으로 로컬 플레이어를 옮긴다.
        /// GameSceneManager/UI_GameScene/GameServerConnectManager 접속은 그대로 유지된다.
        /// 맵/엔트리포인트 이름은 전부 인자로 받으므로, 새 맵을 추가할 때 이 메서드 자체는 건드릴 필요 없이
        /// AddressableAssetKey에 항목을 추가하고 MapPortalController에서 그 키를 가리키기만 하면 된다.
        /// </summary>
        public void SwapMap(AddressableAssetKey _newMapKey, string _entryPointName = "RespawnPoint")
        {
            _ = SwapMapAsync(_newMapKey, _entryPointName);
        }

        /// <summary>
        /// SwapMap의 실제 구현. 순서는 "새 맵 로드 -> 검증 -> 서버에 이동 요청 -> 서버 응답 대기 -> 승인되면 교체"다.
        /// 이전 맵은 서버가 승인한 뒤에야 제거한다. 그 전에 로드 실패/타임아웃/진입 지점 누락이면 요청을 보내지 않고 취소하고,
        /// 서버가 거부해도(포털 범위 밖, 사망 등) 이전 맵을 그대로 둔 채 취소하므로 클라이언트와 서버의 맵이 어긋나지 않고
        /// 플레이어가 맵 없는 빈 공간에 남지도 않는다. 취소해도 포털을 다시 타면 재시도할 수 있다(실패한 로드는 캐시되지 않는다).
        /// 맵을 바꾸는 동안(로딩과 서버 응답 대기 포함)에는 InputBlocker로 플레이어 입력을 잠가, 포털 반경을 벗어나 서버가
        /// 이동을 거부하는 일을 막는다. 진행되는 동안 GameManager의 UI_LoadingBarView를 띄워 화면을 가리고, 끝나면 100%로 채운 뒤 숨긴다.
        /// 실제 교체는 서버 응답을 받는 이벤트 핸들러가 같은 프레임 안에서 끝낸다(HandleMapChangeAcked -> CommitMapSwap) -
        /// 승인 뒤에 이어서 도착한 새 맵의 이벤트(스냅샷, 상자 등)가 교체가 끝난 새 맵에 적용되게 하려는 것이다.
        /// try/finally로 감싸 어떤 경로로 리턴하든(성공/실패/조기 취소) isSwappingMap 해제, 입력 잠금 해제, 로딩바 숨김이
        /// 항상 실행되도록 보장한다 - 맵이 늘어나 이 메서드에 실패 분기가 추가되더라도 깜빡할 여지가 없다.
        /// </summary>
        private async Awaitable SwapMapAsync(AddressableAssetKey _newMapKey, string _entryPointName)
        {
            if (AddressableAssetManager.Instance == null || localPlayerInstance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("SwapMap을 수행할 수 없습니다 (AddressableAssetManager 또는 플레이어가 준비되지 않음).");
                return;
            }

            if (isSwappingMap)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"이전 맵 전환이 아직 끝나지 않아 요청을 무시합니다. 요청한 맵 : {_newMapKey}");
                return;
            }

            isSwappingMap = true;
            GameManager.Instance?.ShowLoadingBar();

            // 맵을 바꾸는 동안(로딩 포함) 이동/공격/단축키 입력을 잠근다. 이 줄은 첫 await 전에 동기적으로 실행되므로, 포털이
            // 지연 구간에 걸어 둔 잠금(MapPortalController)을 풀기 전에 이미 이쪽 잠금이 걸려 있어 빈틈이 없다.
            InputBlocker.SetBlocked(this, true);

            // LoadingBarView는 ObjectPoolManager가 씬 전환 없이 재사용하는 인스턴스라, BootstrapSceneManager가
            // 마지막으로 남긴 타이틀("메인 씬으로 전환 준비 완료" 등)이 지워지지 않은 채 그대로 남아있다.
            // 맵 전환에는 그 문구가 맞지 않으므로 빈 문자열로 지운다.
            GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);

            try
            {
                string newMapKeyString = _newMapKey.ToString();

                const float loadTimeoutSeconds = 30f;
                float loadStartTime = Time.unscaledTime;

                // 1) 이전 맵을 그대로 둔 채 새 맵 프리팹만 먼저 로드한다. 원격 플레이어/몬스터도 아직 이전 맵 소속으로 유효하다.
                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(newMapKeyString);

                // 새 맵을 로드하는 동안이 SwapMap 전체 소요 시간의 대부분을 차지하므로(나머지 단계는 전부 순간적),
                // "0%에 머물다 끝나면 100%로 점프"가 아니라 Addressables가 보고하는 실제 진행률을 그대로 반영한다.
                await AddressableAssetManager.Instance.WaitForLoadAsync(
                    newMapKeyString,
                    () => Time.unscaledTime - loadStartTime >= loadTimeoutSeconds,
                    percentComplete => GameManager.Instance?.LoadingBarView?.UpdateProgress(percentComplete));

                if (this == null || localPlayerInstance == null)
                {
                    return;
                }

                // 2) 교체하기 전에 검증한다. 하나라도 실패하면 이전 맵을 건드리지 않고 취소한다(서버에도 알리지 않는다).
                if (!AddressableAssetManager.Instance.GetHandler(newMapKeyString, out AsyncOperationHandle handle) || handle.Result is not GameObject prefab)
                {
                    bool timedOut = Time.unscaledTime - loadStartTime >= loadTimeoutSeconds;
                    CancelMapSwap(timedOut
                        ? $"맵 로드 시간 초과({loadTimeoutSeconds:0}초) Key : {newMapKeyString}"
                        : $"맵 로드 실패 Key : {newMapKeyString}");
                    return;
                }

                // 프리팹 에셋에서 직접 찾아 확인한다 - 인스턴스를 만들면 이전 맵과 겹쳐 물리/Awake가 먼저 실행되므로, 만들기 전에 알아야 한다.
                if (FindChildRecursive(prefab.transform, _entryPointName) == null)
                {
                    CancelMapSwap($"{newMapKeyString} 맵에서 {_entryPointName}을 찾을 수 없습니다.");
                    return;
                }

                // 로딩하는 사이 사망했으면 요청하지 않는다 - 서버도 사망 중 맵 이동을 거부한다(사망 처리는 별도로 진행되므로
                // 안내 팝업은 띄우지 않는다).
                if (spawnedPlayerModel != null && spawnedPlayerModel.IsDead)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"맵 로딩 중 사망해 맵 이동을 취소합니다. 요청한 맵 : {newMapKeyString}");
                    return;
                }

                var swap = new PendingMapSwap { MapKey = newMapKeyString, EntryPointName = _entryPointName, Prefab = prefab };

                // 서버 연결 기능이 없는 환경(서버 없이 맵만 확인하는 테스트 씬 등)에서는 승인받을 곳이 없으므로 바로 교체한다.
                if (GameServerConnectManager.Instance == null)
                {
                    CommitMapSwap(swap, null);
                    return;
                }

                // 끊긴 상태에서는 요청을 보낼 수 없다(재접속 중이면 재접속 안내 UI가 이미 떠 있다).
                if (!GameServerConnectManager.Instance.IsConnected)
                {
                    CancelMapSwap("서버와 연결되어 있지 않습니다.", showPopup: false);
                    return;
                }

                // 3) 서버에 이동을 요청하고 응답을 기다린다. 이전 맵은 그대로 둔다. 서버는 좌표를 참고하지 않고 맵 데이터의
                // 진입 지점으로 도착 위치를 정하므로, 좌표는 프리팹에 있는 진입 지점 값을 그대로 보낸다.
                Transform prefabEntryPoint = FindChildRecursive(prefab.transform, _entryPointName);
                pendingMapSwap = swap;
                GameServerConnectManager.Instance.SendMapChange(newMapKeyString, prefabEntryPoint.position.x, prefabEntryPoint.position.y, prefabEntryPoint.position.z, prefabEntryPoint.eulerAngles.y);

                float responseDeadline = Time.unscaledTime + MapChangeResponseTimeoutSeconds;
                while (swap.State == MapSwapState.WaitingForServer)
                {
                    if (this == null)
                    {
                        return;
                    }

                    if (GameServerConnectManager.Instance == null || !GameServerConnectManager.Instance.IsConnected)
                    {
                        // 연결이 끊기면 재접속 흐름이 마지막 입장 맵(이전 맵)으로 다시 입장시키므로 어긋나지 않는다.
                        swap.State = MapSwapState.Cancelled;
                        CancelMapSwap("응답을 기다리는 중 서버 연결이 끊어졌습니다.", showPopup: false);
                        break;
                    }

                    if (Time.unscaledTime >= responseDeadline)
                    {
                        // 서버가 승인했는데 응답만 늦는 경우를 대비해 기억해 둔다 - 나중에라도 승인이 오면 서버 기준으로 따라간다.
                        swap.State = MapSwapState.Cancelled;
                        staleMapSwap = swap;
                        CancelMapSwap($"서버 응답 시간 초과({MapChangeResponseTimeoutSeconds:0}초)", showPopup: true);
                        break;
                    }

                    await Awaitable.NextFrameAsync();
                }
            }
            finally
            {
                pendingMapSwap = null;
                isSwappingMap = false;
                InputBlocker.SetBlocked(this, false);
                GameManager.Instance?.HideLoadingBar();
            }
        }

        // 서버 응답을 기다리는 맵 이동 한 건. 새 맵 프리팹은 이미 로드·검증된 상태다.
        private sealed class PendingMapSwap
        {
            public string MapKey;
            public string EntryPointName;
            public GameObject Prefab;
            public MapSwapState State = MapSwapState.WaitingForServer;
        }

        private enum MapSwapState
        {
            WaitingForServer,
            Committed,   // 서버가 승인해 맵을 교체했다
            Rejected,    // 서버가 거부했다(이전 맵 유지)
            Cancelled    // 응답을 받지 못하고 취소했다(이전 맵 유지)
        }

        // 서버가 맵 이동 요청에 응답하기를 기다리는 최대 시간. 서버는 승인/거부 어느 쪽이든 바로 응답하므로 정상이라면 왕복 시간뿐이다.
        private const float MapChangeResponseTimeoutSeconds = 15f;

        private PendingMapSwap pendingMapSwap;

        // 응답 대기 시간이 지나 취소했지만 서버가 승인했을 수 있는 요청. 이후 승인이 도착하면 서버가 이미 그 맵으로 옮겼으므로
        // 클라이언트도 따라간다(HandleMapChangeAcked).
        private PendingMapSwap staleMapSwap;

        /// <summary>
        /// 서버가 맵 이동을 승인했을 때(Game_MapChangeAck) 호출된다. 요청해 둔 이동이 있으면 이 자리에서 바로 교체를 끝낸다 -
        /// 같은 프레임에 이어서 처리되는 새 맵의 이벤트가 교체된 새 맵에 적용되도록 await 없이 동기적으로 처리한다.
        /// </summary>
        private void HandleMapChangeAcked(GameEnterAckPacket ack)
        {
            PendingMapSwap swap = pendingMapSwap != null && pendingMapSwap.State == MapSwapState.WaitingForServer
                ? pendingMapSwap
                : staleMapSwap;

            if (swap == null || ack.Self == null || ack.Self.MapId != swap.MapKey)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"요청하지 않은 맵 이동 승인을 받았습니다 : {ack.Self?.MapId}");
                return;
            }

            if (swap == staleMapSwap)
            {
                // 이미 취소 처리한 요청의 늦은 승인이다. 다른 맵 이동이 진행 중이면 그 흐름과 겹치지 않도록 건드리지 않는다.
                if (isSwappingMap)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"다른 맵 이동 중에 늦은 승인을 받아 무시합니다 : {swap.MapKey}");
                    return;
                }

                staleMapSwap = null;
                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"늦게 도착한 맵 이동 승인에 따라 맵을 교체합니다 : {swap.MapKey}");
            }

            CommitMapSwap(swap, ack);
        }

        /// <summary>
        /// 서버가 맵 이동을 거부했을 때(Game_MapChangeRejected) 호출된다. 서버 상태는 그대로이므로 이전 맵을 유지하고 사유를 알린다.
        /// </summary>
        private void HandleMapChangeRejected(GameMapChangeRejectedPacket packet)
        {
            if (pendingMapSwap == null || pendingMapSwap.State != MapSwapState.WaitingForServer || pendingMapSwap.MapKey != packet.MapId)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"요청하지 않은 맵 이동 거부를 받았습니다 : {packet.MapId} ({packet.Reason})");
                return;
            }

            pendingMapSwap.State = MapSwapState.Rejected;
            DebugLogManager.GenerateErrorMessage<GameSceneManager>($"서버가 맵 이동을 거부했습니다 : {packet.MapId} ({packet.Reason})");

            // 사망 중 거부는 사망 안내가 이미 화면에 있으므로 따로 알리지 않는다.
            if (packet.Reason == MapChangeRejectReason.Dead)
            {
                return;
            }

            string message = packet.Reason switch
            {
                MapChangeRejectReason.NotAtPortal => "포털 가까이에서만 이동할 수 있습니다.",
                MapChangeRejectReason.UnknownMap => "이동할 수 없는 맵입니다.",
                _ => "서버가 맵 이동을 허락하지 않았습니다."
            };
            GameManager.Instance?.ShowAlarmPopup("맵 이동 실패", message);
        }

        /// <summary>
        /// 서버가 승인한 맵 이동을 실제로 적용한다. 여기부터는 되돌리지 않는다. ack가 null이면(서버 연결 기능이 없는 환경)
        /// 프리팹의 진입 지점으로 옮기고, 있으면 서버가 정한 위치/체력으로 맞춘다(HandleGameServerEntered와 같은 처리).
        /// </summary>
        private void CommitMapSwap(PendingMapSwap swap, GameEnterAckPacket ack)
        {
            swap.State = MapSwapState.Committed;

            // 지금 스폰돼있는 원격 플레이어/몬스터는 전부 이전 맵 소속이므로 비운다.
            // 새 맵 목록은 승인에 담겨 온 목록(DispatchMapChangeEntities)으로 다시 채운다.
            RemotePlayerManager.Instance?.ClearAll();
            RemoteMonsterManager.Instance?.ClearAll();
            RemoteChestManager.Instance?.ClearAll();
            RemoteGateManager.Instance?.ClearAll();

            // ClearAll이 파괴한 몬스터를 UI_GameSceneView의 Update() 폴링(Unity null 비교)이 알아서
            // 감지하긴 하지만, 맵 전환 시점에 명시적으로 타겟 정보 패널을 즉시 닫아 경합 프레임을 없앤다.
            monsterTargetView?.ClearTarget();

            if (currentMapInstance != null)
            {
                Destroy(currentMapInstance);
                currentMapInstance = null;
            }

            currentMapInstance = AddressableAssetManager.Instance.InstantiatePrefab(swap.Prefab, transform);
            currentMapId = swap.MapKey;

            if (ack != null)
            {
                // 서버가 맵 데이터로 정한 도착 위치/체력으로 맞춘다 - 클라이언트의 진입 지점과 서버가 아는 위치가 어긋나 이동 거부
                // (위치 보정)가 나는 일이 없다.
                HandleGameServerEntered(ack.Self);
            }
            else
            {
                Transform entryPoint = FindChildRecursive(currentMapInstance.transform, swap.EntryPointName);
                if (entryPoint != null)
                {
                    WarpLocalPlayer(entryPoint.position, entryPoint.rotation);
                }
            }

            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.ConfirmMapChange(swap.MapKey);

                if (ack != null)
                {
                    GameServerConnectManager.Instance.DispatchMapChangeEntities(ack);
                }
            }

            GameManager.Instance?.LoadingBarView?.UpdateProgress(1f);
        }

        /// <summary>
        /// 맵 이동을 취소한 이유를 로그로 남기고(showPopup이면 플레이어에게도 알린다). 이전 맵은 그대로이고 서버 상태도 바뀌지
        /// 않았으므로 상태는 이동 전과 같다 - 포털을 다시 타면 재시도할 수 있다.
        /// </summary>
        private void CancelMapSwap(string _reason, bool showPopup = true)
        {
            DebugLogManager.GenerateErrorMessage<GameSceneManager>($"맵 이동을 취소합니다 : {_reason}");

            if (showPopup)
            {
                GameManager.Instance?.ShowAlarmPopup("맵 이동 실패", "맵을 불러오지 못했거나 서버 응답이 없어 이동하지 못했습니다. 잠시 후 다시 시도해 주세요.");
            }
        }

        #endregion
    }
}
