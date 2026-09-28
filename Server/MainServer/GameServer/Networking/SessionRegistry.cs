using System.Collections.Concurrent;

namespace GameServer.Networking
{
    // 서버 전체에서 "캐릭터 id -> 현재 접속 세션"을 하나만 유지한다. 같은 캐릭터로 다시 접속하면(다른 PC, 재접속 등)
    // 새 세션이 이 자리를 차지하고 이전 세션은 호출측(ClientSession)이 끊는다. 예전에는 이런 추적이 없어서, 이전 세션이
    // 뒤늦게 종료될 때 GameRoom에서 같은 id로 등록된 새 세션을 지워버리고 다른 접속자에게 퇴장까지 알렸다.
    public class SessionRegistry
    {
        private readonly ConcurrentDictionary<long, ClientSession> _sessionsByPlayerId = new();

        // session을 playerId의 현재 세션으로 등록하고, 이전에 등록돼 있던 다른 세션이 있으면 돌려준다.
        public ClientSession? Register(long playerId, ClientSession session)
        {
            ClientSession? previous = null;
            _sessionsByPlayerId.AddOrUpdate(
                playerId,
                session,
                (_, existing) =>
                {
                    previous = ReferenceEquals(existing, session) ? null : existing;
                    return session;
                });
            return previous;
        }

        // session이 아직 playerId의 현재 세션일 때만 등록을 해제한다(이미 새 세션으로 바뀌었으면 건드리지 않는다).
        public void Unregister(long playerId, ClientSession session)
        {
            _sessionsByPlayerId.TryRemove(new KeyValuePair<long, ClientSession>(playerId, session));
        }
    }
}
