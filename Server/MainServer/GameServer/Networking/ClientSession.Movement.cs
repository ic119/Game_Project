using GameServer.Logging;
using Microsoft.Extensions.Logging;
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
    // 이동 요청 검증(이동 거리 예산)과 위치 보정.
    public partial class ClientSession
    {
        // 이동 속도 검증 - "이동 거리 예산" 방식. 예산은 초당 MoveBudgetRefillPerSecond(m)씩 차고 최대 MoveBudgetCapacity(m)까지
        // 쌓이며, 이동 요청마다 직전 인정 위치로부터의 수평 이동 거리만큼 쓴다. 클라이언트 PlayerMoveController 기본값
        // (걷기 5m/s, 대시 0.25초에 3m + 쿨다운 1초 → 지속 최대 약 8m/s)보다 여유 있게 잡아 정상 이동과 네트워크
        // 지연으로 몰려 들어온 패킷은 통과시키고, 스피드핵/순간이동만 거부한다. 이동 속도 버프 등이 생기면 함께 조정해야 한다.
        // 높이(Y)는 서버에 지형 정보가 없어 검증하지 않는다(알려진 한계).
        private const float MoveBudgetRefillPerSecond = 9f;
        private const float MoveBudgetCapacity = 5f;
        private float _moveBudget = MoveBudgetCapacity;
        private DateTime _lastMoveBudgetRefillAtUtc = DateTime.UtcNow;

        // 거부된 이동이 연달아 오면(보정 패킷이 도착하기 전 이미 보낸 이동들) 보정 패킷을 매번 보내지 않도록 제한한다.
        private static readonly TimeSpan PositionCorrectionInterval = TimeSpan.FromMilliseconds(500);
        private DateTime _lastPositionCorrectionAtUtc = DateTime.MinValue;

        // 룸의 위치를 갱신한다(다른 접속자에게는 방 틱의 스냅샷으로 전달된다).
        // request.PlayerId가 이 세션의 실제 플레이어와 같은지 검증한다 - 그렇지 않으면 다른 플레이어의
        // ID를 실어 보내는 것만으로 그 플레이어를 임의의 위치로 옮길 수 있다(다른 핸들러들과 동일한 검증).
        // 클라이언트 PlayerNetworkSender는 0.1초마다(초당 10회) 보낸다. 그보다 여유 있게 허용하고, 초과분은 브로드캐스트 없이 버린다 -
        // 이동은 방 전체로 퍼지므로 거리 0짜리 이동을 도배해도 다른 세션들의 전송 대기열이 찬다.
        private readonly RequestRateLimiter _moveRateLimiter = new(capacity: 20, refillPerSecond: 15);

        private void HandleMoveRequest(byte[] body)
        {
            if (!_moveRateLimiter.TryAcquire())
            {
                return;
            }

            var request = C2SMoveRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room
                || !room.TryGetInfo(playerId, out var info))
            {
                return;
            }

            // 사망 중에는 움직일 수 없다(클라이언트도 조작을 막는다). 부활 위치는 서버가 정한다(GameRoom.ReviveAfterDelayAsync).
            if (info.CurrentHp <= 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            float elapsedSeconds = (float)(now - _lastMoveBudgetRefillAtUtc).TotalSeconds;
            _lastMoveBudgetRefillAtUtc = now;
            _moveBudget = Math.Min(MoveBudgetCapacity, _moveBudget + elapsedSeconds * MoveBudgetRefillPerSecond);

            float dx = request.X - info.X;
            float dz = request.Z - info.Z;
            float horizontalDistance = MathF.Sqrt(dx * dx + dz * dz);

            // 같은 맵 안 좌표 이동 포탈은 예산과 무관하게 허용한다(예산도 쓰지 않는다).
            if (horizontalDistance > _moveBudget && _mapId is { } mapId && IsCoordinateTeleport(mapId, info, request.X, request.Z))
            {
                horizontalDistance = 0f;
            }

            if (horizontalDistance > _moveBudget)
            {
                Log.LogDebug("이동 거부 (PlayerId={PlayerId}) : {Distance:F2}m > 허용 {Budget:F2}m", playerId, horizontalDistance, _moveBudget);
                SendPositionCorrection(info, now);
                return;
            }

            // 위치만 갱신한다. 다른 접속자에게는 방 틱이 다음 스냅샷(S2CWorldSnapshot)에 모아서 보낸다.
            _moveBudget -= horizontalDistance;
            room.UpdatePosition(request.PlayerId, request.X, request.Y, request.Z, request.RotationY);
        }

        // 거부된 이동을 되돌리도록 본인에게 서버가 마지막으로 인정한 위치를 보낸다(PositionCorrectionInterval로 제한).
        private void SendPositionCorrection(PlayerInfo info, DateTime now)
        {
            if (now - _lastPositionCorrectionAtUtc < PositionCorrectionInterval)
            {
                return;
            }
            _lastPositionCorrectionAtUtc = now;

            var correction = new S2CPositionCorrection { X = info.X, Y = info.Y, Z = info.Z, RotationY = info.RotationY };
            Send(OpCode.Game_PositionCorrection, correction.Encode());
        }
    }
}
