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
    // 입장(Game_EnterRequest) 인증/등록과 입장 거부, 이전 세션 상태 복원.
    public partial class ClientSession
    {
        // mapId에 해당하는 GameRoom(없으면 새로 생성)에 자신을 등록하고, 본인에게는 시야 안의 기존
        // 접속자/몬스터 목록(Game_EnterAck)을 보낸다. 주변 플레이어는 다음 방 틱에 "시야 진입"(Game_PlayerJoined)으로 받는다.
        // 등록 전에 AccessToken이 info.PlayerId(characterId)를 실제로 소유한 계정의 것인지 AuthServer에
        // 확인한다 - 그렇지 않으면 누구나 임의의 PlayerId를 자칭해 접속/조작할 수 있기 때문이다.
        private static readonly TimeSpan PendingRewardWaitTimeout = TimeSpan.FromSeconds(10);

        private async Task HandleEnterRequestAsync(byte[] body, CancellationToken ct)
        {
            // 한 세션은 한 번만 입장한다(이미 입장한 세션의 재요청은 무시).
            if (_playerId is not null)
            {
                return;
            }

            var request = C2SEnterRequest.Decode(body);
            var info = request.Player;

            // 이 캐릭터의 이전 세션에서 저장이 끝나지 않은 처치 보상이 있으면 먼저 기다린다 - 그 전에 DB를 읽으면 옛 level/exp로
            // 시작하고, 이후 이 세션의 처치 보상이 옛 값 기준의 최종 level/exp로 저장돼 진행도가 되돌아간다(KillRewardSaver 참고).
            if (!await _killRewardSaver.WaitForPendingAsync(info.PlayerId, PendingRewardWaitTimeout, ct))
            {
                RejectEnter($"Game_EnterRequest 이전 처치 보상 저장 대기 시간 초과 (PlayerId={info.PlayerId})",
                    "이전 플레이 기록을 저장하는 중입니다. 잠시 후 다시 접속해 주세요.");
            }

            CharacterSnapshot? snapshot = await _authValidator.FetchOwnedCharacterAsync(request.AccessToken, info.PlayerId, ct);
            if (snapshot is null)
            {
                RejectEnter($"Game_EnterRequest 인증 실패 (PlayerId={info.PlayerId})", "인증에 실패했습니다.");
                return;
            }

            // 맵 데이터(MapData/{mapId}.json)에 등록된 맵에만 입장할 수 있다. 그렇지 않으면 임의의 mapId 문자열마다
            // 새 GameRoom이 생겨(메모리/AI 루프 낭비), 서버가 좌표를 모르는 맵에서는 입장/부활/포탈 검증도 할 수 없다.
            if (!MapDataCatalog.TryGet(info.MapId, out MapData mapData) || mapData.RespawnPoint is not { } respawnPoint)
            {
                RejectEnter($"Game_EnterRequest 알 수 없는 맵 (PlayerId={info.PlayerId}, MapId={info.MapId})", "알 수 없는 맵입니다.");
                return;
            }

            _playerId = info.PlayerId;

            // info의 닉네임/외형/레벨/경험치/전투 스탯은 클라이언트가 채워 보낸 값이라 위조 가능하다(PlayerInfo.cs 주석 참고).
            // MainServer에서 방금 받아온 snapshot(DB 원본)으로 전부 덮어쓰고, 클라이언트 값은 위치/맵만 사용한다 -
            // 이후 이 값이 GameRoom에 저장되고, 경험치 계산(처치 보상)과 다른 접속자 브로드캐스트에 그대로 쓰인다.
            info.Nickname = snapshot.Nickname;
            info.HairIndex = snapshot.HairIndex;
            info.EyeIndex = snapshot.EyeIndex;
            info.MouthIndex = snapshot.MouthIndex;
            info.Level = snapshot.Level;
            info.Exp = snapshot.Exp;
            PlayerCombatStats.ApplySnapshot(info, snapshot);

            // 외형 장비(무기/갑옷/투구)도 DB 원본으로 정한다 - 다른 접속자에게 보이는 모습이라 클라이언트 값을 믿지 않는다.
            EquippedVisuals equipped = EquippedVisuals.From(snapshot, ItemCatalog.Exists);
            info.WeaponItemId = equipped.WeaponItemId;
            info.ArmorItemId = equipped.ArmorItemId;
            info.HelmetItemId = equipped.HelmetItemId;

            // 체력은 DB에 저장하지 않는다. 기본은 가득 찬 상태로 시작하고(클라이언트 HealthComponent.ApplyFromUserStats와 동일),
            // 최근에 끊긴 상태가 있으면 아래에서 이어받는다.
            info.MaxHp = CombatStatCalculator.CalculateMaxHp(snapshot);
            info.CurrentHp = info.MaxHp;

            // 입장 위치도 서버가 맵 데이터로 정한다(클라이언트도 같은 RespawnPoint에 스폰한다). 클라이언트 좌표를 그대로
            // 받으면 입장 순간에 원하는 곳으로 순간이동할 수 있다.
            info.X = respawnPoint.X;
            info.Y = respawnPoint.Y;
            info.Z = respawnPoint.Z;
            info.RotationY = respawnPoint.RotationY;

            // 같은 캐릭터로 이미 접속 중인 세션이 있으면 끊는다(새 접속이 우선). 이전 세션은 방 항목이 이 세션으로 교체된 뒤
            // 종료되더라도 GameRoom.Remove(playerId, session)가 자기 항목만 지우므로 새 세션에는 영향이 없다.
            // 끊기 전에 그 세션의 현재 상태를 넘겨받는다 - 다른 곳에서 다시 접속하는 것으로 체력을 회복할 수 없게 한다.
            PlayerStateSnapshot? previousState = null;
            if (_sessions.Register(info.PlayerId, this) is { } previousSession)
            {
                Log.LogWarning("중복 접속 (PlayerId={PlayerId}) - 이전 세션을 종료합니다.", info.PlayerId);
                previousState = previousSession.CaptureState();
                previousSession.Kick("다른 곳에서 같은 캐릭터로 접속하여 연결이 종료되었습니다.");
            }

            // 보관된 상태는 항상 꺼낸다(이전 세션에서 넘겨받았더라도 남은 옛 항목이 다음 입장에 쓰이지 않게).
            PlayerStateSnapshot? storedState = _disconnectedStates.Take(info.PlayerId);
            RestorePreviousState(info, previousState ?? storedState);

            GameRoom room = _mapRooms.GetOrCreate(info.MapId);
            _room = room;
            _mapId = info.MapId;

            var (visiblePlayers, visibleMonsters) = room.Join(info, this);

            var ack = new S2CEnterAck { Self = info, ExistingPlayers = visiblePlayers, ExistingMonsters = visibleMonsters };
            Send(OpCode.Game_EnterAck, ack.Encode());

            SendChestState(room);
        }

        // 상자는 몬스터/플레이어처럼 관심 영역(AOI)으로 걸러 보내지 않는다 - 맵에 소수뿐이라 방 전체 상태를 그대로 알려줘도
        // 부담이 없다. 입장/맵 이동 직후 서 있는 상자 목록 -> 이미 열린 상자 순으로 보낸다(GameRoom.SendChestState). 이후의 변화
        // (열림/제거/리스폰)는 방 전체 브로드캐스트로 온다. 이미 열린 상자는 Game_ChestOpenBroadcast를 그대로 재사용해, 실시간으로
        // 여는 경우와 클라이언트 처리 코드가 완전히 같다(TreasureChestInteractionController 입장에서는 구분할 필요가 없다).
        // 던전 게이트 위치(방 생성 시 후보 중 뽑힌 한 곳)도 같은 시점에 알린다 - 상자 목록 바로 뒤에 온다.
        private void SendChestState(GameRoom room)
        {
            room.SendChestState(this);
            room.SendGateState(this);
        }

        // 입장하는 캐릭터에 이전 상태(끊기기 전, 또는 밀어낸 세션의 현재 상태)를 이어받게 한다. info는 가득 찬 체력과
        // 요청한 맵의 부활 지점으로 채워진 상태로 들어온다.
        // - 사망한 채 끊겼으면 그대로 둔다: 부활 지점에서 가득 찬 체력으로 시작하는 것은 사망 후 자동 부활과 같은 결과다.
        // - 살아 있었으면 체력을 이어받는다(최대 체력을 넘지 않게). 같은 맵으로 들어오면 위치도 이어받는다 - 서버가 마지막으로
        //   인정한 위치라 순간이동이 되지 않는다. 다른 맵으로 들어오면(로비에서 새로 시작 등) 그 맵의 부활 지점에서 시작한다.
        private static void RestorePreviousState(PlayerInfo info, PlayerStateSnapshot? state)
        {
            if (state is null || state.CurrentHp <= 0)
            {
                return;
            }

            info.CurrentHp = Math.Min(state.CurrentHp, info.MaxHp);

            if (state.MapId == info.MapId)
            {
                info.X = state.X;
                info.Y = state.Y;
                info.Z = state.Z;
                info.RotationY = state.RotationY;
            }
        }

        // 입장 거부: 사유를 System_Error로 알리고 연결을 끊는다. 사유는 RunAsync의 finally가 소켓을 닫기 전에 전송 대기열을
        // 비우면서 나간다. 소켓을 여기서 직접 닫지 않는 건 수신 루프가 닫힌 스트림을 읽다 예외를 내지 않게 하기 위함이다.
        [DoesNotReturn]
        private void RejectEnter(string logMessage, string clientMessage)
        {
            Log.LogWarning("{Reason} - 연결을 종료합니다.", logMessage);
            Send(OpCode.System_Error, Encoding.UTF8.GetBytes(clientMessage));
            throw new EnterRejectedException();
        }

        // Game_EnterRequest 거부(인증 실패/알 수 없는 맵)를 RunAsync의 루프 종료 신호로 쓰기 위한 내부 전용 예외.
        private sealed class EnterRejectedException : Exception
        {
        }
    }
}
