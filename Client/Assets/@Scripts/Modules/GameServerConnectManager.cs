using Incheol.Modules.Networking;
using Incheol.Utils;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// GameServer(TCP)에 접속해 자신의 캐릭터를 입장시키고, 다른 접속자의 입장/퇴장/이동을 이벤트로 알린다.
    /// ServerConnectManager(HTTP, Auth 서버)와는 별개의 접속으로, GameScene에 있는 동안만 연결을 유지한다.
    /// 수신 루프는 백그라운드 스레드에서 돌기 때문에 Unity API를 직접 건드릴 수 없다 - 디코딩된 이벤트는
    /// _pendingActions 큐에 넣고 Update()에서 메인 스레드로 드레인해서 호출한다.
    /// </summary>
    public class GameServerConnectManager : SingletonObject<GameServerConnectManager>
    {
        [Header("Game 서버(TCP) 접속 설정")]
        [SerializeField] private string host = "localhost";
        [SerializeField] private int port = 9000;

        protected override bool PersistAcrossScenes => true;

        private TcpClient tcpClient;
        private NetworkStream stream;
        private CancellationTokenSource cts;
        private readonly ConcurrentQueue<Action> pendingActions = new();

        private long localPlayerId;
        private bool isConnected;

        public event Action<GamePlayerInfo> OnPlayerJoined;
        public event Action<long> OnPlayerLeft;
        public event Action<GameMoveBroadcastPacket> OnPlayerMoved;

        #region LifeCycle
        private void Update()
        {
            while (pendingActions.TryDequeue(out Action action))
            {
                action();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Disconnect();
        }
        #endregion

        #region Method
        /// <summary>
        /// GameServer에 접속하고 자신의 캐릭터 정보를 Game_EnterRequest로 전송한다.
        /// 접속 실패 시 DebugLogManager로 에러를 남기고 조용히 리턴한다(멀티 입장은 부가 기능이므로
        /// 로그인/게임 진행 자체를 막지 않는다).
        /// </summary>
        public void ConnectAndEnter(GamePlayerInfo localInfo)
        {
            _ = ConnectAndEnterAsync(localInfo);
        }

        private async Awaitable ConnectAndEnterAsync(GamePlayerInfo localInfo)
        {
            if (isConnected)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>("이미 GameServer에 접속되어 있습니다.");
                return;
            }

            try
            {
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                stream = tcpClient.GetStream();
                cts = new CancellationTokenSource();
                localPlayerId = localInfo.PlayerId;
                isConnected = true;

                _ = ReadLoopAsync(cts.Token);

                await SendAsync(GameOpCode.Game_EnterRequest, localInfo.Encode());
            }
            catch (Exception exception)
            {
                DebugLogManager.GenerateErrorMessage<GameServerConnectManager>($"GameServer 접속 실패 : {exception.Message}");
                isConnected = false;
            }
        }

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

        public void Disconnect()
        {
            if (!isConnected)
            {
                return;
            }

            isConnected = false;
            cts?.Cancel();
            stream?.Close();
            tcpClient?.Close();
        }

        private async Task SendAsync(GameOpCode opCode, byte[] body)
        {
            if (stream == null)
            {
                return;
            }

            try
            {
                byte[] frame = GamePacketFrame.Encode((ushort)opCode, body);
                await stream.WriteAsync(frame, cts.Token);
            }
            catch (Exception)
            {
                // 접속이 끊긴 상태에서의 전송 실패는 ReadLoopAsync 쪽에서 이미 처리(연결 종료)하므로 여기서는 무시한다.
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await GamePacketFrame.ReadFrameAsync(stream, ct);
                    if (frame is null)
                    {
                        break;
                    }

                    HandleFrame(frame.Value.OpCode, frame.Value.Body);
                }
            }
            catch (Exception)
            {
                // 서버 종료/네트워크 단절 - 정상적인 종료 경로로 취급한다.
            }
            finally
            {
                isConnected = false;
            }
        }

        private void HandleFrame(ushort opCode, byte[] body)
        {
            switch ((GameOpCode)opCode)
            {
                case GameOpCode.Game_EnterAck:
                    var ack = GameEnterAckPacket.Decode(body);
                    foreach (GamePlayerInfo player in ack.ExistingPlayers)
                    {
                        pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(player));
                    }
                    break;

                case GameOpCode.Game_PlayerJoined:
                    var joined = GamePlayerJoinedPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerJoined?.Invoke(joined.Player));
                    break;

                case GameOpCode.Game_PlayerLeft:
                    var left = GamePlayerLeftPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerLeft?.Invoke(left.PlayerId));
                    break;

                case GameOpCode.Game_MoveBroadcast:
                    var move = GameMoveBroadcastPacket.Decode(body);
                    pendingActions.Enqueue(() => OnPlayerMoved?.Invoke(move));
                    break;
            }
        }
        #endregion
    }
}
