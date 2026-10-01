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
    // 접속/입장/재접속 흐름과 연결 종료.
    public partial class GameServerConnectManager
    {
        #region Method
        /// <summary>
        /// GameServer에 접속하고 자신의 캐릭터 정보를 Game_EnterRequest로 전송한다.
        /// 접속 실패 시 DebugLogManager로 에러를 남기고 조용히 리턴한다(멀티 입장은 부가 기능이므로
        /// 로그인/게임 진행 자체를 막지 않는다).
        /// </summary>
        public void ConnectAndEnter(GamePlayerInfo localInfo)
        {
            lastEnterInfo = localInfo;
            reconnectAttempt = 0;
            hasEntered = false;
            _ = ConnectAndEnterAsync(localInfo, ++connectionGeneration);
        }

        // 접속과 입장 요청 전송까지 성공하면 true. 입장이 받아들여졌는지는 Game_EnterAck(OnEntered)로 안다.
        // 그 사이 Disconnect()나 새 접속으로 generation이 바뀌었으면 만든 연결을 닫고 false를 반환한다.
        private async Awaitable<bool> ConnectAndEnterAsync(GamePlayerInfo localInfo, int generation)
        {
            if (isConnected)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("이미 GameServer에 접속되어 있습니다.");
                return false;
            }

            // GameServer는 입장 시 이 토큰으로 MainServer에 캐릭터 소유권을 확인한다. 로비에 오래 머물렀으면 이미 만료됐을 수
            // 있어(30분) AccessToken 속성을 그대로 쓰지 않고, 만료가 임박하면 재발급한 토큰을 받는다.
            string accessToken = ServerConnectManager.Instance != null ? await ServerConnectManager.Instance.GetValidAccessTokenAsync() : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("로그인 세션이 없어 GameServer에 접속할 수 없습니다.");
                return false;
            }

            var newTcpClient = new TcpClient();
            try
            {
                await newTcpClient.ConnectAsync(host, port);

                // GameServer(ClientSession.RunAsync)가 접속을 받자마자 TLS 핸드셰이크부터 요구하므로,
                // 프레임을 하나라도 보내기 전에 SslStream으로 감싸고 인증을 마쳐야 한다.
                var sslStream = new SslStream(newTcpClient.GetStream(), leaveInnerStreamOpen: false, ValidateServerCertificate);
                await sslStream.AuthenticateAsClientAsync(host);

                if (generation != connectionGeneration)
                {
                    // 접속하는 사이 Disconnect()(씬 전환 등)가 호출됐다 - 이 연결은 쓰지 않는다.
                    sslStream.Close();
                    newTcpClient.Close();
                    return false;
                }

                tcpClient = newTcpClient;
                stream = sslStream;
                cts = new CancellationTokenSource();
                localPlayerId = localInfo.PlayerId;
                isConnected = true;
                intentionalDisconnect = false;
                wasKicked = false;
                ServerClock.Reset();
                heartbeatSendTimer = 0f;
                timeSinceLastHeartbeatAck = 0f;

                _ = ReadLoopAsync(sslStream, newTcpClient, generation, cts.Token);

                // AccessToken은 본인 인증에만 쓰이므로 다른 플레이어에게도 브로드캐스트되는 GamePlayerInfo가 아니라
                // Game_EnterRequest 전용 래퍼(GameEnterRequestPacket)에만 담아 보낸다.
                var enterRequest = new GameEnterRequestPacket { AccessToken = accessToken, Player = localInfo };
                await SendAsync(GameOpCode.Game_EnterRequest, enterRequest.Encode());
                return true;
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 접속 실패 : {exception.Message}");
                newTcpClient.Close();
                return false;
            }
        }

        /// <summary>
        /// 연결이 예기치 않게 끊겼을 때(메인 스레드) 다음 재접속을 예약한다. 시도를 다 썼거나, 이번 게임에서 한 번도 입장하지
        /// 못했으면(인증 실패 등 - 다시 시도해도 같다) OnDisconnected로 알린다.
        /// </summary>
        private void ScheduleReconnect()
        {
            if (lastEnterInfo == null || !hasEntered || reconnectAttempt >= ReconnectDelaysSeconds.Length)
            {
                reconnectAttempt = 0;
                OnDisconnected?.Invoke();
                return;
            }

            float delay = ReconnectDelaysSeconds[reconnectAttempt];
            reconnectAttempt++;
            OnReconnecting?.Invoke(reconnectAttempt, ReconnectDelaysSeconds.Length);
            _ = ReconnectAfterDelayAsync(delay, connectionGeneration);
        }

        private async Awaitable ReconnectAfterDelayAsync(float delaySeconds, int generation)
        {
            await Awaitable.WaitForSecondsAsync(delaySeconds);

            // 기다리는 사이 Disconnect()(씬 전환/로그아웃)가 호출됐으면 재접속하지 않는다.
            if (this == null || generation != connectionGeneration)
            {
                return;
            }

            DebugLogManager.GenerateLogMessage<GameServerConnectManager>($"GameServer 재접속 시도 ({reconnectAttempt}/{ReconnectDelaysSeconds.Length})");

            // 접속 자체가 실패하면 수신 루프가 없어 끊김 알림도 오지 않으므로 여기서 바로 다음 시도를 예약한다.
            // 접속은 됐지만 입장이 거부되면(서버가 System_Error 후 연결을 닫음) 수신 루프 종료가 다음 시도를 예약한다.
            if (!await ConnectAndEnterAsync(lastEnterInfo, ++connectionGeneration) && generation + 1 == connectionGeneration)
            {
                ScheduleReconnect();
            }
        }

        public void Disconnect()
        {
            // 연결이 없어도(재접속 대기 중) 번호를 바꿔 예약된 재접속을 취소한다.
            connectionGeneration++;
            reconnectAttempt = 0;
            lastEnterInfo = null;

            if (!isConnected)
            {
                return;
            }

            intentionalDisconnect = true;
            isConnected = false;
            cts?.Cancel();
            stream?.Close();
            tcpClient?.Close();
        }

        // 하트비트 타임아웃 등 연결이 예기치 않게 끝났을 때 쓴다. Disconnect()와 달리
        // intentionalDisconnect를 설정하지 않아서 ReadLoopAsync의 finally가 OnDisconnected를 발화하게 된다.
        private void CloseConnectionUnexpectedly()
        {
            isConnected = false;
            cts?.Cancel();
            stream?.Close();
            tcpClient?.Close();
        }
        #endregion
    }
}
