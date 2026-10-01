using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using GameServer.Combat;
using GameServer.Items;
using GameServer.Maps;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // 맵 이동(Game_MapChangeRequest) 검증: 포탈 근처 여부, 승인/거부.
    public partial class ClientSession
    {
        // mapId 맵에서 targetMapId로 가는 MapSwap 포탈 중 player가 범위 안에 있는 것을 찾는다(없으면 null).
        private static MapPortal? FindMapSwapPortal(string mapId, string targetMapId, PlayerInfo player)
        {
            if (!MapDataCatalog.TryGet(mapId, out MapData mapData))
            {
                return null;
            }

            return mapData.Portals.FirstOrDefault(portal =>
                portal.Type == MapPortal.MapSwapType
                && portal.TargetMapId == targetMapId
                && portal.IsWithinRange(player.X, player.Z));
        }

        // 같은 맵 안 좌표 이동 포탈을 탄 이동인지 확인한다: 서버가 마지막으로 인정한 위치가 CoordinateTeleport 포탈 범위 안이고,
        // 요청한 위치가 그 포탈의 목적지 근처면 속도 검증 대상이 아닌 정상 순간이동으로 본다.
        private const float TeleportDestinationTolerance = 2f;

        private static bool IsCoordinateTeleport(string mapId, PlayerInfo lastAccepted, float x, float z)
        {
            if (!MapDataCatalog.TryGet(mapId, out MapData mapData))
            {
                return false;
            }

            foreach (MapPortal portal in mapData.Portals)
            {
                if (portal.Type != MapPortal.CoordinateTeleportType || portal.Destination is not { } destination
                    || !portal.IsWithinRange(lastAccepted.X, lastAccepted.Z))
                {
                    continue;
                }

                float dx = x - destination.X;
                float dz = z - destination.Z;
                if (dx * dx + dz * dz <= TeleportDestinationTolerance * TeleportDestinationTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        // 같은 접속을 유지한 채 다른 맵으로 옮긴다: 이전 맵 방에서 빠지며(보던 사람들에게 Game_PlayerLeft) 새 맵 방에 들어가
        // 시야 안의 기존 접속자/몬스터 목록(Game_MapChangeAck)을 받는다. 새 맵의 주변 플레이어는 다음 틱에 "시야 진입"으로 받는다.
        private void HandleMapChangeRequest(byte[] body)
        {
            var request = C2SMapChangeRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId
                || _room is not { } previousRoom || _mapId is not { } previousMapId)
            {
                return;
            }

            // 방에 없는 세션(같은 캐릭터의 새 세션이 이미 방 항목을 차지했다)은 곧 끊기므로 응답하지 않는다.
            if (!previousRoom.TryGetInfo(playerId, out var info))
            {
                return;
            }

            // 사망 중에는 맵을 옮기지 않는다 - 부활 예약(GameRoom.ReviveAfterDelayAsync)이 사망한 방에 걸려 있어,
            // 다른 방으로 옮겨 가면 부활 알림이 새 방에 전달되지 않는다. 클라이언트도 사망 중에는 포탈을 막지만, 로딩하는 사이에
            // 죽었을 수 있다 - 응답이 없으면 클라이언트가 계속 기다리므로 반드시 거부 응답을 보낸다.
            if (info.CurrentHp <= 0)
            {
                RejectMapChange(request.MapId, MapChangeRejectReason.Dead, $"[GameServer] 맵 이동 거부 (PlayerId={playerId}) : 사망 중, {previousMapId} -> {request.MapId}");
                return;
            }

            // 현재 맵에서 요청한 맵으로 가는 MapSwap 포탈 근처에 있을 때만 옮겨 준다. 도착 위치도 클라이언트가 보낸 좌표가 아니라
            // 맵 데이터의 진입 지점으로 정한다 - 그렇지 않으면 아무 맵의 아무 좌표로나 순간이동할 수 있다.
            // 클라이언트는 이 응답(승인/거부)을 받은 뒤에야 맵을 교체하므로, 여기서 거부해도 클라이언트와 서버의 맵이 어긋나지 않는다.
            // 정상 클라이언트는 같은 맵 데이터로 포탈을 타므로 거부될 일이 드물다(맵 데이터를 다시 내보내지 않은 경우 제외).
            MapPortal? portal = FindMapSwapPortal(previousMapId, request.MapId, info);
            if (!MapDataCatalog.TryGet(request.MapId, out _))
            {
                RejectMapChange(request.MapId, MapChangeRejectReason.UnknownMap, $"[GameServer] 맵 이동 거부 (PlayerId={playerId}) : 알 수 없는 맵, {previousMapId} -> {request.MapId}");
                return;
            }

            if (portal?.Destination is not { } destination)
            {
                RejectMapChange(request.MapId, MapChangeRejectReason.NotAtPortal, $"[GameServer] 맵 이동 거부 (PlayerId={playerId}) : {previousMapId} -> {request.MapId}, 위치=({info.X:F1},{info.Z:F1})");
                return;
            }

            // 같은 캐릭터의 새 세션이 방 항목을 이미 차지했다면(이 세션은 곧 끊긴다) 옮기지 않는다.
            if (!previousRoom.Remove(playerId, this))
            {
                return;
            }

            // HP 등 나머지 전투 스탯은 PlayerInfo 인스턴스를 그대로 재사용해 유지하고, 위치/맵만 갱신한다.
            info.MapId = request.MapId;
            info.X = destination.X;
            info.Y = destination.Y;
            info.Z = destination.Z;
            info.RotationY = destination.RotationY;

            GameRoom nextRoom = _mapRooms.GetOrCreate(request.MapId);
            var (visiblePlayers, visibleMonsters) = nextRoom.Join(info, this);

            _room = nextRoom;
            _mapId = request.MapId;

            var ack = new S2CEnterAck { Self = info, ExistingPlayers = visiblePlayers, ExistingMonsters = visibleMonsters };
            Send(OpCode.Game_MapChangeAck, ack.Encode());

            SendChestState(nextRoom);
        }

        // 맵 이동 요청을 거부하고 사유를 알린다. 서버 상태(방, 위치)는 건드리지 않는다.
        private void RejectMapChange(string requestedMapId, MapChangeRejectReason reason, string logMessage)
        {
            Console.WriteLine(logMessage);
            Send(OpCode.Game_MapChangeRejected, new S2CMapChangeRejected { MapId = requestedMapId, Reason = reason }.Encode());
        }
    }
}
