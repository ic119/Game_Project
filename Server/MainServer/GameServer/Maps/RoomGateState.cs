using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Maps
{
    // 방(GameRoom) 하나의 던전 게이트 상태: 후보 지점 중 방 생성 시 뽑힌 한 곳. 상자(RoomChestState)와 달리 뽑은 뒤에는 방이
    // 살아 있는 동안 바뀌지 않으므로 잠금/타이머가 필요 없다(생성자에서만 정해지는 불변 상태).
    // GameRoom에서 분리한 이유는 GameRoom(틱 루프/접속자 등) 없이 선택 규칙과 포탈 판정을 테스트하기 위해서다.
    public class RoomGateState
    {
        // 이 방에 서 있는 게이트 후보. 후보가 없는 맵이면 null(게이트 없음).
        public MapGateCandidate? ActiveGate { get; }

        // ActiveGate를 맵 이동 검증용 MapSwap 포탈로 바꾼 것. 클라이언트 MapPortalController가 SetTargetMap으로 같은 도착지를 쓰므로
        // 서버도 후보 위치 + 게이트 설정의 도착 정보로 똑같이 판정한다(ClientSession.FindMapSwapPortal).
        public MapPortal? ActivePortal { get; }

        public RoomGateState(MapData? mapData, Random random)
        {
            if (mapData is null || mapData.GatePlan is null)
            {
                return;
            }

            ActiveGate = Select(mapData.GateCandidates, random);
            if (ActiveGate is null)
            {
                return;
            }

            ActivePortal = new MapPortal
            {
                X = ActiveGate.X,
                Y = ActiveGate.Y,
                Z = ActiveGate.Z,
                Radius = ActiveGate.Radius,
                Type = MapPortal.MapSwapType,
                TargetMapId = mapData.GatePlan.TargetMapId,
                Destination = mapData.GatePlan.Destination
            };
        }

        // 후보 중 균등한 확률로 정확히 하나를 뽑는다. 후보가 없으면 null. 순수 함수라 시드를 고정해 결과를 재현할 수 있다.
        public static MapGateCandidate? Select(IReadOnlyList<MapGateCandidate> candidates, Random random)
        {
            return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
        }

        // 방에 새로 입장/맵 이동한 세션에게 이 방의 게이트 위치를 알린다. 게이트가 없는 맵이어도 "없음"을 보낸다 -
        // 클라이언트가 이전 맵의 게이트 상태를 남기지 않고 항상 이 응답 하나로 정리할 수 있다.
        public void SendState(Action<OpCode, byte[]> send)
        {
            var packet = new S2CActiveGate();
            if (ActiveGate is not null)
            {
                packet.HasGate = true;
                packet.Id = ActiveGate.Id;
                packet.X = ActiveGate.X;
                packet.Y = ActiveGate.Y;
                packet.Z = ActiveGate.Z;
            }

            send(OpCode.Game_ActiveGateNotify, packet.Encode());
        }
    }
}
