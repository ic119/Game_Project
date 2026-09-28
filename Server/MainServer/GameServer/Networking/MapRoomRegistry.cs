using System.Collections.Concurrent;
using System.Threading;
using GameServer.Monsters;

namespace GameServer.Networking
{
    // mapId별로 GameRoom을 하나씩 관리한다. 브로드캐스트가 맵 단위로 격리되는 건
    // ClientSession이 항상 "현재 맵의 GameRoom"만 통해 송수신하기 때문이다(GameRoom 자체는 수정 불필요).
    //
    // 방은 한 번 만들어지면 서버가 종료될 때까지 유지한다(맵마다 하나). 입장/맵 이동이 맵 데이터에 등록된 맵으로만
    // 허용되므로(ClientSession) 방 개수는 맵 개수로 제한된다. 예전에는 비면 레지스트리에서 지웠는데, GameRoom의
    // AI 루프/리스폰 타이머는 서버 수명 토큰에 묶여 계속 돌아서 맵이 비었다 찰 때마다 방이 하나씩 누적됐고,
    // "비었는지 확인 -> 제거" 사이에 다른 세션이 그 방에 들어오면 플레이어가 있는 방이 목록에서 사라지는 경합도 있었다.
    // 빈 방의 몬스터는 그대로 남아 있다가 다음 입장자에게 보인다(몬스터가 적은 현재 규모에서 AI 틱 비용은 무시할 만하다).
    public class MapRoomRegistry
    {
        // GetOrAdd의 생성 함수는 동시에 호출되면 여러 번 실행될 수 있다(ConcurrentDictionary 사양). GameRoom 생성자는
        // 몬스터 스폰과 AI 루프 시작까지 하므로, 버려지는 인스턴스가 생기면 그 AI 루프가 서버 종료까지 계속 돈다 -
        // Lazy로 감싸 실제 생성은 한 번만 일어나게 한다.
        private readonly ConcurrentDictionary<string, Lazy<GameRoom>> _rooms = new();
        private readonly CancellationToken _serverLifetimeCt;

        // 몬스터 리스폰 타이머(GameRoom 내부)가 개별 요청 ct가 아니라 서버 전체 수명에 묶이도록,
        // 서버 시작 시 한 번 받은 취소 토큰을 새로 만드는 모든 GameRoom에 그대로 물려준다.
        public MapRoomRegistry(CancellationToken serverLifetimeCt)
        {
            _serverLifetimeCt = serverLifetimeCt;
        }

        public GameRoom GetOrCreate(string mapId)
        {
            return _rooms.GetOrAdd(mapId, id => new Lazy<GameRoom>(
                () => new GameRoom(id, MonsterSpawnCatalog.GetPointsForMap(id), _serverLifetimeCt),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
    }
}
