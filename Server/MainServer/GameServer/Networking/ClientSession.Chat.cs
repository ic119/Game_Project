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
    // 채팅 요청 처리와 도배 제한.
    public partial class ClientSession
    {
        // Move와 달리 발신자 본인 화면에도 같은 메시지가 떠야 하므로 BroadcastToAll을 쓴다.
        // 닉네임은 클라이언트를 신뢰하지 않고 룸에 등록된(Game_EnterRequest 시점) 값을 서버가 직접 채운다.
        private const int MaxChatMessageLength = 200;

        // 채팅 도배 제한: 연달아 5개까지, 이후 초당 1개. 초과분은 조용히 버린다(방 전체로 퍼지는 요청이라 대기열 보호 목적도 있다).
        private readonly RequestRateLimiter _chatRateLimiter = new(capacity: 5, refillPerSecond: 1);

        private void HandleChatRequest(byte[] body)
        {
            if (!_chatRateLimiter.TryAcquire())
            {
                return;
            }

            var request = C2SChatRequest.Decode(body);

            if (_playerId is not { } playerId || request.PlayerId != playerId || _room is not { } room)
            {
                return;
            }

            var message = request.Message?.Trim() ?? string.Empty;
            if (message.Length == 0)
            {
                return;
            }

            if (message.Length > MaxChatMessageLength)
            {
                message = message[..MaxChatMessageLength];
            }

            var nickname = room.TryGetInfo(playerId, out var info) ? info.Nickname : string.Empty;

            var broadcast = new S2CChatBroadcast
            {
                PlayerId = playerId,
                Nickname = nickname,
                Message = message,
                Timestamp = request.Timestamp
            };

            room.BroadcastToAll(OpCode.Game_ChatBroadcast, broadcast.Encode());
        }
    }
}
