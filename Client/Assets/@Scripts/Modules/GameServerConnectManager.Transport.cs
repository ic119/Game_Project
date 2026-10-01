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
    // 저수준 송신(SendAsync), 수신 루프, 프레임 분배(HandleFrame).
    public partial class GameServerConnectManager
    {
        #region Method
        private async Task SendAsync(GameOpCode opCode, byte[] body)
        {
            if (stream == null)
            {
                return;
            }

            byte[] frame = GamePacketFrame.Encode((ushort)opCode, body);
            CancellationToken ct = cts.Token;
            bool lockTaken = false;

            try
            {
                // 이동(주기 전송)/하트비트/공격/채팅이 모두 fire-and-forget으로 이 메서드를 호출하므로, 앞선 쓰기가
                // 끝나기 전에 다음 쓰기가 시작될 수 있다. SslStream은 동시 쓰기를 지원하지 않아 프레임이 섞이거나
                // NotSupportedException이 나므로, 서버 ClientSession.SendAsync와 같은 방식으로 한 번에 하나씩 쓴다.
                await writeLock.WaitAsync(ct);
                lockTaken = true;
                await stream.WriteAsync(frame, ct);
            }
            catch (OperationCanceledException)
            {
                // Disconnect()로 연결을 정리하는 중 - 대기 중이던 전송은 조용히 버린다.
            }
            catch (Exception exception)
            {
                // 접속이 끊긴 상태에서의 전송 실패는 ReadLoopAsync 쪽에서 이미 처리하지만, 디버깅을 위해 로그는 남겨둔다.
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 전송 실패 opCode={opCode} : {exception.Message}");
            }
            finally
            {
                if (lockTaken)
                {
                    writeLock.Release();
                }
            }
        }

        // 연결 하나의 수신 루프. 그 연결의 스트림/소켓과 번호(generation)를 직접 받는다 - 필드(stream/tcpClient)는 재접속하면
        // 새 연결로 바뀌므로, 늦게 끝난 이전 루프가 필드를 닫으면 새 연결이 끊긴다.
        private async Task ReadLoopAsync(Stream connectionStream, TcpClient connectionClient, int generation, CancellationToken ct)
        {
            // 마지막으로 해석하던 프레임의 OpCode(오류 로그용).
            ushort lastOpCode = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await GamePacketFrame.ReadFrameAsync(connectionStream, ct);
                    if (frame is null)
                    {
                        break;
                    }

                    lastOpCode = frame.Value.OpCode;
                    HandleFrame(frame.Value.OpCode, frame.Value.Body);
                }
            }
            catch (Exception exception) when (exception is IOException and not EndOfStreamException
                                               || exception is ObjectDisposedException or OperationCanceledException)
            {
                // 서버 종료/네트워크 단절/직접 Disconnect() - 정상적인 종료 경로로 취급한다.
            }
            catch (Exception exception)
            {
                // 잘못된 프레임 길이(InvalidDataException), 형식이 맞지 않는 바디(EndOfStreamException 등). 서버와 클라이언트의
                // 패킷 정의가 어긋났을 가능성이 크다 - 예전에는 이것도 "정상 종료"로 삼켜 원인을 알 수 없었다.
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 수신 데이터 처리 실패로 연결을 끊습니다 (opCode=0x{lastOpCode:X4}) : {exception}");
            }
            finally
            {
                // 오류로 루프가 끝났을 때도 소켓을 확실히 닫는다(서버가 끊은 경우 다시 닫아도 무해하다).
                connectionStream.Close();
                connectionClient.Close();

                // 번호 확인은 메인 스레드에서 한다(connectionGeneration은 메인 스레드에서만 바뀐다). 그 사이 Disconnect()나
                // 새 연결로 번호가 바뀌었으면 이 연결은 이미 버려진 것이라 아무 것도 하지 않는다.
                bool endedUnexpectedly = !intentionalDisconnect && !wasKicked;
                pendingActions.Enqueue(() =>
                {
                    if (generation != connectionGeneration)
                    {
                        return;
                    }

                    isConnected = false;

                    if (endedUnexpectedly)
                    {
                        ScheduleReconnect();
                    }
                });
            }
        }

        private void HandleFrame(ushort opCode, byte[] body)
        {
            switch ((GameOpCode)opCode)
            {
                case GameOpCode.Game_EnterAck:
                    var ack = GameEnterAckPacket.Decode(body);
                    pendingActions.Enqueue(() =>
                    {
                        hasEntered = true;
                        OnEntered?.Invoke(ack.Self);
                    });
                    foreach (GamePlayerInfo player in ack.ExistingPlayers)
                    {
                        pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(player));
                    }
                    foreach (GameMonsterInfo monster in ack.ExistingMonsters)
                    {
                        pendingActions.Enqueue(() => OnMonsterSpawned?.Invoke(monster));
                    }
                    pendingActions.Enqueue(() =>
                    {
                        if (reconnectAttempt > 0)
                        {
                            reconnectAttempt = 0;
                            OnReconnected?.Invoke();
                        }
                    });
                    break;

                // 페이로드 구조가 Game_EnterAck과 동일하므로(본인 상태 + 새 맵의 기존 접속자/몬스터 목록) 같은 디코더를 재사용한다.
                // 맵 이동은 서버 승인(이 응답)을 받은 뒤에야 클라이언트가 맵을 교체하므로, 플레이어/몬스터를 여기서 바로 스폰하지
                // 않고 한 번에 넘긴다 - 받은 쪽(GameSceneManager)이 이전 맵 정리와 새 맵 생성을 마친 뒤 DispatchMapChangeEntities로 적용한다.
                case GameOpCode.Game_MapChangeAck:
                    var mapChangeAck = GameEnterAckPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMapChangeAcked?.Invoke(mapChangeAck));
                    break;

                case GameOpCode.Game_MapChangeRejected:
                    var mapChangeRejected = GameMapChangeRejectedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMapChangeRejected?.Invoke(mapChangeRejected));
                    break;

                case GameOpCode.Game_PlayerJoined:
                    var joined = GamePlayerJoinedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(joined.Player));
                    break;

                case GameOpCode.Game_PlayerLeft:
                    var left = GamePlayerLeftPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerLeft?.Invoke(left.PlayerId));
                    break;

                case GameOpCode.Game_WorldSnapshot:
                    var snapshot = GameWorldSnapshotPacket.Decode(body);
                    // 서버 시각 추정(ServerClock)은 메인 스레드에서만 다루므로 이벤트와 같은 액션 안에서 먼저 갱신한다 -
                    // 구독자(원격 개체 보간)가 이 스냅샷을 쓰는 시점에는 이미 반영돼 있다.
                    pendingActions.Enqueue(() =>
                    {
                        ServerClock.Observe(snapshot.ServerTimeMs);
                        OnWorldSnapshot?.Invoke(snapshot);
                    });
                    break;

                case GameOpCode.Game_MonsterLeaveView:
                    var monsterLeaveView = GameMonsterLeaveViewPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterLeftView?.Invoke(monsterLeaveView.MonsterId));
                    break;

                case GameOpCode.Game_ChatBroadcast:
                    var chat = GameChatBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChatReceived?.Invoke(chat));
                    break;

                case GameOpCode.Game_DamageBroadcast:
                    var damage = GameDamageBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnDamageReceived?.Invoke(damage));
                    break;

                case GameOpCode.Game_AttackAnimationBroadcast:
                    var attackAnimation = GameAttackAnimationBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnAttackAnimationReceived?.Invoke(attackAnimation));
                    break;

                case GameOpCode.Game_ChestOpenBroadcast:
                    var chestOpened = GameChestOpenBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChestOpened?.Invoke(chestOpened));
                    break;

                case GameOpCode.Game_ActiveChestsNotify:
                    var activeChests = GameActiveChestsPacket.Decode(body);
                    pendingActions.Enqueue(() => OnActiveChestsReceived?.Invoke(activeChests));
                    break;

                case GameOpCode.Game_ChestSpawnBroadcast:
                    var chestSpawn = GameChestSpawnPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChestSpawned?.Invoke(chestSpawn.Chest));
                    break;

                case GameOpCode.Game_EquipmentChangedBroadcast:
                    var equipmentChanged = GameEquipmentChangedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnEquipmentChanged?.Invoke(equipmentChanged));
                    break;

                case GameOpCode.Game_ChestDespawnBroadcast:
                    var chestDespawn = GameChestDespawnPacket.Decode(body);
                    pendingActions.Enqueue(() => OnChestDespawned?.Invoke(chestDespawn.ChestId));
                    break;

                case GameOpCode.Game_MonsterSpawnBroadcast:
                    var monsterSpawn = GameMonsterSpawnBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterSpawned?.Invoke(monsterSpawn.Monster));
                    break;

                case GameOpCode.Game_MonsterDamageBroadcast:
                    var monsterDamage = GameMonsterDamageBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterDamaged?.Invoke(monsterDamage));
                    break;

                case GameOpCode.Game_MonsterDieBroadcast:
                    var monsterDie = GameMonsterDieBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterDied?.Invoke(monsterDie));
                    break;

                case GameOpCode.Game_MonsterAttackBroadcast:
                    var monsterAttack = GameMonsterAttackBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterAttacked?.Invoke(monsterAttack));
                    break;

                case GameOpCode.Game_MonsterAttackStartBroadcast:
                    var monsterAttackStart = GameMonsterAttackStartBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterAttackStarted?.Invoke(monsterAttackStart));
                    break;

                case GameOpCode.Game_MonsterAttackDodgedBroadcast:
                    var monsterAttackDodged = GameMonsterAttackDodgedBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnMonsterAttackDodged?.Invoke(monsterAttackDodged));
                    break;

                case GameOpCode.Game_ExpGainBroadcast:
                    var expGain = GameExpGainBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnExpGained?.Invoke(expGain));
                    break;

                case GameOpCode.Game_LootBroadcast:
                    var loot = GameLootBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnLootReceived?.Invoke(loot));
                    break;

                case GameOpCode.Game_PlayerHpBroadcast:
                    var hpChanged = GamePlayerHpBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerHpChanged?.Invoke(hpChanged));
                    break;

                case GameOpCode.Game_PlayerRevived:
                    var revived = GamePlayerRevivedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerRevived?.Invoke(revived));
                    break;

                case GameOpCode.Game_UseItemResult:
                    var useItemResult = GameUseItemResultPacket.Decode(body);
                    pendingActions.Enqueue(() => OnUseItemResult?.Invoke(useItemResult));
                    break;

                case GameOpCode.Game_PositionCorrection:
                    var positionCorrection = GamePositionCorrectionPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPositionCorrected?.Invoke(positionCorrection));
                    break;

                // 클라이언트가 주기적으로 보낸 System_Heartbeat에 대한 서버 응답이다 - 타임아웃 타이머를 초기화한다.
                case GameOpCode.System_Heartbeat:
                    pendingActions.Enqueue(() => timeSinceLastHeartbeatAck = 0f);
                    break;

                // 서버가 Game_EnterRequest 인증 실패 등으로 연결을 끊기 직전에 보낸다(현재는 인증 실패 사유뿐).
                case GameOpCode.System_Error:
                    string errorMessage = Encoding.UTF8.GetString(body);
                    pendingActions.Enqueue(() => OnServerError?.Invoke(errorMessage));
                    break;

                // 서버가 이 연결을 강제로 끊기 직전에 보낸다(현재는 같은 캐릭터 중복 접속). 곧 연결이 닫히지만
                // OnDisconnected 대신 이 사유만 알린다.
                case GameOpCode.System_Kicked:
                    wasKicked = true;
                    string kickReason = Encoding.UTF8.GetString(body);
                    pendingActions.Enqueue(() => OnKicked?.Invoke(kickReason));
                    break;
            }
        }
        #endregion
    }
}
