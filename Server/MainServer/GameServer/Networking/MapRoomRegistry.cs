using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GameServer.Networking
{
    // mapId별로 GameRoom을 하나씩 관리한다. 브로드캐스트가 맵 단위로 격리되는 건
    // ClientSession이 항상 "현재 맵의 GameRoom"만 통해 송수신하기 때문이다(GameRoom 자체는 수정 불필요).
    public class MapRoomRegistry
    {
        private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

        public GameRoom GetOrCreate(string mapId)
        {
            return _rooms.GetOrAdd(mapId, _ => new GameRoom());
        }

        // 방금 나간 방이 비어있으면 레지스트리에서 제거한다. 그 사이 다른 세션이 같은 mapId로
        // 새 방을 만들었을 수 있으므로, 넘겨받은 room 인스턴스와 현재 등록된 인스턴스가 같을 때만 지운다.
        public void RemoveIfEmpty(string mapId, GameRoom room)
        {
            if (room.IsEmpty)
            {
                ((ICollection<KeyValuePair<string, GameRoom>>)_rooms).Remove(new KeyValuePair<string, GameRoom>(mapId, room));
            }
        }
    }
}
