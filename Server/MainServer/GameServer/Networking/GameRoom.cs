using GameServer.Logging;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Combat;
using GameServer.Maps;
using GameServer.Monsters;
using Shared;
using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Networking
{
    // ApplyMonsterAttack의 결과. GainedExp가 채워져 있으면(몬스터가 죽고 공격자를 찾은 경우)
    // 호출측(ClientSession)이 공격자 본인에게만 Game_ExpGainBroadcast를 보내야 한다는 뜻이다.
    public readonly struct MonsterAttackResult
    {
        public bool MonsterDied { get; init; }
        public int? GainedExp { get; init; }
        public int NewLevel { get; init; }
        public bool DidLevelUp { get; init; }
        public int ExpToNextLevel { get; init; }

        // 처치 시 DropTableCatalog.Roll로 계산된 보상. 공격자를 찾지 못해 GainedExp가 비어도(즉시 return된
        // 경로) 드롭은 이미 굴려진 상태이므로 별도로 채워진다 - 몬스터 처치 자체는 성립했기 때문이다.
        public int GainedGold { get; init; }
        public IReadOnlyList<(string ItemId, int Qty)>? DroppedItems { get; init; }
    }

    // 하나의 맵(mapId)에 속한 접속자들의 그룹. MapRoomRegistry가 맵마다 이 인스턴스를 하나씩 관리한다.
    // 접속자 레지스트리에 더해 이 맵의 몬스터 스폰/전투/리스폰, 방 틱(스냅샷 전송)까지 담당한다.
    // 몬스터는 소유 클라이언트가 없으므로(플레이어와 달리) 스탯/HP를 GameServer가 직접 들고 권위를 가진다.
    // 누가 누구를 보는지(관심 영역, 입장/퇴장/시야 진입·이탈, 대상별 알림)는 GameRoom.Visibility.cs에 있다.
    public partial class GameRoom
    {
        private static readonly ILogger Log = GameLog.For<GameRoom>();

        private readonly ConcurrentDictionary<long, (PlayerInfo Info, ISessionSender Session)> _players = new();

        // 몬스터 id -> 현재 상태(+ 어느 스폰 포인트 소속인지). 포인트 단위 개체수/리스폰 판정에 쓴다.
        private readonly ConcurrentDictionary<long, MonsterRuntime> _monsters = new();

        // 서버 종료 시 대기 중인 리스폰 타이머(Task.Delay)를 함께 취소하기 위한 토큰.
        // 개별 요청(Game_MonsterAttackRequest 등)의 ct와 달리, 리스폰은 특정 요청에 종속되지 않는
        // 방 자체의 백그라운드 작업이라 서버 전체 수명 토큰을 별도로 받아 둔다.
        private readonly CancellationToken _serverLifetimeCt;

        // 이 방의 맵 id. 부활 위치 등 맵 좌표 데이터(MapDataCatalog)를 찾는 키다.
        private readonly string _mapId;

        public GameRoom(string mapId, List<MonsterSpawnPointDefinition> spawnPoints, CancellationToken serverLifetimeCt)
        {
            _mapId = mapId;
            _serverLifetimeCt = serverLifetimeCt;

            // 방이 만들어지는 시점(첫 플레이어 입장)에 각 포인트를 최대 개체수까지 즉시 채운다.
            // 아직 아무도 접속하지 않은 시점이라 브로드캐스트가 필요 없다 - 입장자는 S2CEnterAck의
            // ExistingMonsters(시야 안의 몬스터)로 받는다.
            foreach (var point in spawnPoints)
            {
                for (int i = 0; i < point.MaxAlive; i++)
                {
                    SpawnMonsterAtPoint(point);
                }
            }

            MapDataCatalog.TryGet(mapId, out MapData? mapData);
            _chests = new RoomChestState(mapData, Random.Shared, BroadcastToAll, _serverLifetimeCt);
            _gate = new RoomGateState(mapData, Random.Shared);

            _ = RunTickLoopAsync(_serverLifetimeCt);
        }

        // 이 방의 상자 상태(서 있는 상자/열린 상자/제거·리스폰 타이머). 고정 상자 + 후보 중 방 생성 시 뽑힌 상자로 시작하고,
        // 후보 상자는 열린 뒤 제거되고 다른 후보에 다시 생긴다(RoomChestState 주석 참고).
        private readonly RoomChestState _chests;

        // 방에 새로 입장/맵 이동한 세션에게 상자 상태(서 있는 목록 -> 이미 열린 것)를 알린다.
        public void SendChestState(ISessionSender session) => _chests.SendState(session.Send);

        // 이 방의 던전 게이트(후보 중 방 생성 시 뽑힌 한 곳). 방이 살아 있는 동안 바뀌지 않는다(RoomGateState 주석 참고).
        private readonly RoomGateState _gate;

        // 방에 새로 입장/맵 이동한 세션에게 게이트 위치를 알린다.
        public void SendGateState(ISessionSender session) => _gate.SendState(session.Send);

        // 맵 이동 검증용: 이 방의 게이트를 MapSwap 포탈로 바꾼 것(게이트가 없으면 null).
        public MapPortal? ActiveGatePortal => _gate.ActivePortal;

        // 직전 틱 이후 위치가 바뀐 플레이어 id. 이동 요청은 위치만 갱신하고 여기에 표시하며, 실제 전송은 다음 틱의
        // 스냅샷(S2CWorldSnapshot)에 모아서 한다(RunTickLoopAsync). 값은 쓰지 않는다(ConcurrentDictionary를 집합으로 사용).
        private readonly ConcurrentDictionary<long, byte> _movedPlayerIds = new();

        public void UpdatePosition(long playerId, float x, float y, float z, float rotationY)
        {
            if (_players.TryGetValue(playerId, out var entry))
            {
                entry.Info.X = x;
                entry.Info.Y = y;
                entry.Info.Z = z;
                entry.Info.RotationY = rotationY;
                _movedPlayerIds[playerId] = 0;
            }
        }

        public bool TryGetMonsterPosition(long monsterId, out (float X, float Y, float Z) position)
        {
            if (_monsters.TryGetValue(monsterId, out var runtime))
            {
                position = (runtime.Info.X, runtime.Info.Y, runtime.Info.Z);
                return true;
            }

            position = default;
            return false;
        }

        // 맵 전체(발신자 포함)에 보낸다 - 현재는 채팅만 쓴다. 나머지 알림은 관심 영역 안의 사람에게만 보낸다
        // (GameRoom.Visibility.cs의 SendToViewersOfPlayer/SendToViewersOfMonster).
        // 브로드캐스트는 각 세션의 전송 대기열에 넣기만 하고 바로 돌아온다(ClientSession.Send). 실제 소켓 쓰기는 세션마다
        // 전송 루프가 따로 하므로, 느린 클라이언트 한 명이 방 전체 전송이나 AI 틱을 붙잡거나, 한 세션의 전송 오류가
        // 호출측(다른 플레이어의 요청 처리, AI 루프)으로 번지지 않는다.
        public void BroadcastToAll(OpCode opCode, byte[] body)
        {
            foreach (var (_, session) in _players.Values)
            {
                session.Send(opCode, body);
            }
        }

        public bool TryGetInfo(long playerId, [NotNullWhen(true)] out PlayerInfo? info)
        {
            if (_players.TryGetValue(playerId, out var entry))
            {
                info = entry.Info;
                return true;
            }

            info = null;
            return false;
        }

        // 상자를 연다. 존재하지 않는 상자/사거리 밖/이미 열린 상자면 실패(false)로 조용히 거부한다 -
        // ClientSession.HandleChestOpenRequest가 이 경우 아무것도 보내지 않는다(ChestId 위조, 지금 서 있지 않은 후보의 id 등도
        // 여기서 걸러진다). 선착순 판정은 RoomChestState의 잠금 안에서 원자적으로 이뤄진다.
        public bool TryOpenChest(string chestId, long playerId, out int gold, out List<(string ItemId, int Qty)> items)
        {
            gold = 0;
            items = new List<(string, int)>();

            if (!_players.TryGetValue(playerId, out var entry)
                || !_chests.TryOpen(chestId, entry.Info.X, entry.Info.Z, out MapChest? chest))
            {
                return false;
            }

            (gold, items) = DropTableCatalog.Roll(chest!.LootTableKey);
            return true;
        }

        // 인벤토리에서 장비를 장착/해제해 바뀐 공격력/방어력을 반영한다(Game_StatUpdateRequest). PlayerInfo가
        // class(참조 타입)라 이 메서드로 값만 바꿔주면 ApplyMonsterAttack/AttackPlayer가 다음 판정부터
        // 곧바로 새 값을 쓴다 - Game_EnterRequest 스냅샷 이후 갱신 경로가 이것뿐이므로, 호출하지 않으면 세션 내내
        // 접속 시점 스탯으로 고정된다. 다른 접속자에게 알릴 필요는 없다(PvP 피해도 서버가 이 값으로 계산해 결과만 보낸다).
        // snapshot(DB 원본)의 기본 스탯/장비를 서버 전용 기준값으로 갱신하고, 현재 레벨(접속 중 오른 레벨 포함) 기준으로 다시 계산한다.
        public bool TryUpdateCombatStats(long playerId, CharacterSnapshot snapshot)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            PlayerCombatStats.ApplySnapshot(entry.Info, snapshot);
            return true;
        }

        // 장착 장비(무기/갑옷/투구)가 바뀌었으면 PlayerInfo에 반영하고 그 플레이어를 보고 있는 사람(과 본인)에게 알려
        // 외형을 동기화한다(Game_EquipmentChangedBroadcast). 바뀐 게 없으면 아무것도 보내지 않는다. 막 시야에 들어오는 사람은
        // 갱신된 PlayerInfo(Game_PlayerJoined)로 같은 값을 받는다.
        public bool TryUpdateEquipment(long playerId, EquippedVisuals equipped)
        {
            if (!_players.TryGetValue(playerId, out var entry))
            {
                return false;
            }

            PlayerInfo info = entry.Info;
            if (info.WeaponItemId == equipped.WeaponItemId
                && info.ArmorItemId == equipped.ArmorItemId
                && info.HelmetItemId == equipped.HelmetItemId)
            {
                return false;
            }

            info.WeaponItemId = equipped.WeaponItemId;
            info.ArmorItemId = equipped.ArmorItemId;
            info.HelmetItemId = equipped.HelmetItemId;

            var broadcast = new S2CEquipmentChangedBroadcast
            {
                PlayerId = playerId,
                WeaponItemId = equipped.WeaponItemId,
                ArmorItemId = equipped.ArmorItemId,
                HelmetItemId = equipped.HelmetItemId
            };
            SendToViewersOfPlayer(playerId, OpCode.Game_EquipmentChangedBroadcast, broadcast.Encode());
            return true;
        }
    }
}
