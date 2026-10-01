using Incheol.Modules.Networking;
using Incheol.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Incheol.Modules
{
    // 서버로 보내는 요청 API(이동, 채팅, 공격, 대쉬, 아이템 등)와 맵 이동 확인.
    public partial class GameServerConnectManager
    {
        #region Method
        /// <summary>
        /// 자신의 현재 위치/회전을 GameServer에 보낸다(Game_MoveRequest). 접속 전이면 아무 것도 하지 않는다.
        /// </summary>
        public void SendMove(float x, float y, float z, float rotationY)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameMoveRequestPacket
            {
                PlayerId = localPlayerId,
                X = x,
                Y = y,
                Z = z,
                RotationY = rotationY,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_MoveRequest, request.Encode());
        }

        /// <summary>
        /// 채팅 메시지를 GameServer에 보낸다(Game_ChatRequest). 닉네임은 서버가 룸 등록 정보로 채우므로 보내지 않는다.
        /// 접속 전이거나 빈 문자열이면 아무 것도 하지 않는다.
        /// </summary>
        public void SendChat(string message)
        {
            if (!isConnected || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var request = new GameChatRequestPacket
            {
                PlayerId = localPlayerId,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_ChatRequest, request.Encode());
        }

        /// <summary>서버와 연결돼 있는지(재접속 중이거나 끊겼으면 false).</summary>
        public bool IsConnected => isConnected;

        /// <summary>
        /// 다른 맵으로 이동해도 되는지 GameServer에 요청한다(Game_MapChangeRequest). 서버가 승인하면 이전 맵 방에서 빼고 새 맵 방에
        /// 등록한 뒤 Game_MapChangeAck(OnMapChangeAcked)를, 거부하면 Game_MapChangeRejected(OnMapChangeRejected)를 돌려준다.
        /// 클라이언트는 이 응답을 받은 뒤에 맵을 교체한다(GameSceneManager.SwapMapAsync) - 요청을 보내는 시점에는 새 맵을
        /// 이미 로드해 두되 교체는 하지 않은 상태다. 서버는 좌표를 참고하지 않고 맵 데이터의 진입 지점으로 도착 위치를 정한다.
        /// 재접속 시 입장할 맵은 서버가 승인한 뒤 ConfirmMapChange로 바꾼다(요청만 보낸 시점에는 바꾸지 않는다).
        /// </summary>
        public void SendMapChange(string mapId, float x, float y, float z, float rotationY)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameMapChangeRequestPacket
            {
                PlayerId = localPlayerId,
                MapId = mapId,
                X = x,
                Y = y,
                Z = z,
                RotationY = rotationY
            };

            _ = SendAsync(GameOpCode.Game_MapChangeRequest, request.Encode());
        }

        /// <summary>
        /// 서버가 맵 이동을 승인해 클라이언트가 맵을 교체했을 때 호출한다. 재접속하면 지금 있는 맵으로 다시 입장해야 하므로
        /// 마지막 입장 정보의 맵을 바꾼다. 요청을 보낸 시점이 아니라 승인된 뒤에 바꿔야, 거부되거나 응답이 오지 않았을 때
        /// 재접속이 서버에 없는 맵으로 입장하려 하지 않는다.
        /// </summary>
        public void ConfirmMapChange(string mapId)
        {
            if (lastEnterInfo != null)
            {
                lastEnterInfo.MapId = mapId;
            }
        }

        /// <summary>
        /// 맵 이동 승인(OnMapChangeAcked)에 담겨 온 새 맵 시야 안의 플레이어/몬스터를 스폰 이벤트로 알린다. 맵 교체를 마친 뒤
        /// 호출해야 한다(교체하면서 이전 맵의 원격 개체를 비우기 때문에, 먼저 스폰하면 함께 지워진다).
        /// </summary>
        public void DispatchMapChangeEntities(GameEnterAckPacket ack)
        {
            foreach (GamePlayerInfo player in ack.ExistingPlayers)
            {
                OnPlayerJoined?.Invoke(player);
            }

            foreach (GameMonsterInfo monster in ack.ExistingMonsters)
            {
                OnMonsterSpawned?.Invoke(monster);
            }
        }

        /// <summary>
        /// 공격 의사를 GameServer에 보낸다(Game_AttackRequest). 데미지 수치는 보내지 않는다 - 서버가
        /// Game_EnterRequest 때 등록된 자신의 AttackPower를 사용해 그대로 중계하고, 방어력 차감은
        /// 각 클라이언트가 target의 로컬 Defense로 직접 계산한다.
        /// </summary>
        public void SendAttack(long targetId)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameAttackRequestPacket
            {
                AttackerId = localPlayerId,
                TargetId = targetId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_AttackRequest, request.Encode());
        }

        /// <summary>
        /// 대쉬 시작을 GameServer에 알린다(Game_DashRequest). 서버가 쿨다운을 검증한 뒤 짧은 무적 구간을 기록하고,
        /// 그 구간(또는 선딜 도중 대쉬해 사거리를 벗어난 경우)에 도착한 몬스터 공격을 회피로 판정한다.
        /// 대쉬 이동 자체는 클라이언트가 하고, 이 알림은 무적 판정의 근거로만 쓰인다.
        /// </summary>
        public void SendDash()
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameDashRequestPacket { PlayerId = localPlayerId };
            _ = SendAsync(GameOpCode.Game_DashRequest, request.Encode());
        }

        /// <summary>
        /// 공격 모션을 GameServer에 알린다(Game_AttackAnimationRequest). SendAttack/SendMonsterAttack과 달리
        /// 대상 유무와 무관하게 콤보 타수마다(허공 스윙 포함) 매번 호출해야 한다 - 근처 다른 플레이어가
        /// 내 스윙 모션 자체를 볼 수 있어야 하기 때문이다. 데미지 판정에는 전혀 쓰이지 않는 순수 연출용이다.
        /// weaponType은 현재 장착 무기(WeaponType enum 값)를 그대로 담아 보낸다 - 서버는 해석하지 않고
        /// 그대로 중계하며, 받는 쪽(RemoteCharacterController)이 이 값으로 무기별 애니메이션을 고른다.
        /// </summary>
        public void SendAttackAnimation(int comboStage, WeaponType weaponType)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameAttackAnimationRequestPacket
            {
                AttackerId = localPlayerId,
                ComboStage = comboStage,
                WeaponType = (int)weaponType
            };

            _ = SendAsync(GameOpCode.Game_AttackAnimationRequest, request.Encode());
        }

        /// <summary>
        /// 보물상자 개봉을 GameServer에 요청한다(Game_ChestOpenRequest). chestId는 MapData/{mapId}.json의
        /// chests[].id와 정확히 일치해야 한다(TreasureChestInteractionController.chestId, MapDataExporter가 내보낸 값).
        /// 결과는 즉시 돌아오지 않고 OnChestOpened(성공 시) 또는 아무 반응 없음(실패 시 - 이미 열렸거나 사거리 밖)으로 온다.
        /// </summary>
        public void SendChestOpenRequest(string chestId)
        {
            if (!isConnected || string.IsNullOrEmpty(chestId))
            {
                return;
            }

            var request = new GameChestOpenRequestPacket { ChestId = chestId };
            _ = SendAsync(GameOpCode.Game_ChestOpenRequest, request.Encode());
        }

        /// <summary>
        /// 몬스터에 대한 공격 의사를 GameServer에 보낸다(Game_MonsterAttackRequest). 플레이어 공격(SendAttack)과
        /// 달리 데미지 계산은 서버가 직접 수행한다 - 몬스터는 소유 클라이언트가 없어 로컬 Defense로 계산할
        /// 대상이 없기 때문이다. 결과는 OnMonsterDamaged(RemainingHp 포함)로 돌아온다.
        /// </summary>
        public void SendMonsterAttack(long monsterId)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameMonsterAttackRequestPacket
            {
                AttackerId = localPlayerId,
                MonsterId = monsterId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _ = SendAsync(GameOpCode.Game_MonsterAttackRequest, request.Encode());
        }

        /// <summary>
        /// 인벤토리에서 장비를 장착/해제해 바뀐 공격력/방어력을 GameServer에 알린다(Game_StatUpdateRequest).
        /// 서버는 이 값을 Game_EnterRequest 때와 같은 신뢰 수준으로 그대로 캐싱만 하고 응답하지 않는다.
        /// 접속 전이면(아직 GameScene 진입 전, 또는 이미 끊긴 상태) 아무 것도 하지 않는다 - 그 경우 최신 값은
        /// 다음 Game_EnterRequest(재접속)에 자연히 실려 간다.
        /// </summary>
        public void SendStatUpdate(int attackPower, int defense)
        {
            if (!isConnected)
            {
                return;
            }

            var request = new GameStatUpdateRequestPacket
            {
                PlayerId = localPlayerId,
                AttackPower = attackPower,
                Defense = defense
            };

            _ = SendAsync(GameOpCode.Game_StatUpdateRequest, request.Encode());
        }

        /// <summary>
        /// 소비 아이템(물약 등) 사용을 GameServer에 요청한다(Game_UseItemRequest). 회복과 아이템 차감은 서버가 하고,
        /// 결과는 OnUseItemResult(차감 여부), 회복된 체력은 OnPlayerHpChanged로 돌아온다.
        /// 접속 전이면 보내지 않고 false를 반환한다 - 호출측이 응답을 기다리는 상태로 남지 않게 하기 위함이다.
        /// </summary>
        public bool SendUseItem(string itemId)
        {
            if (!isConnected)
            {
                return false;
            }

            var request = new GameUseItemRequestPacket { PlayerId = localPlayerId, ItemId = itemId };
            _ = SendAsync(GameOpCode.Game_UseItemRequest, request.Encode());
            return true;
        }
        #endregion
    }
}
