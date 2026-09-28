using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 다른 플레이어의 캐릭터를 표현한다. 입력을 직접 받는 PlayerMoveController와 달리, GameServerConnectManager가 수신한
    /// Game_WorldSnapshot 위치를 서버 시각과 함께 쌓아 두고(SnapshotInterpolationBuffer), 서버 시각 기준 조금 과거
    /// (InterpolationDelayMs) 위치를 앞뒤 두 스냅샷 사이에서 보간해 그린다 - 패킷 도착 간격이 흔들려도 일정한 속도로 움직인다.
    /// </summary>
    public class RemoteCharacterController : MonoBehaviour
    {
        [Tooltip("보간된 이동 속도(m/s)가 이 값을 넘으면 이동 애니메이션(IsMove)을 재생한다.")]
        [SerializeField, Min(0f)] private float moveSpeedThreshold = 0.2f;

        [Tooltip("멈춘 뒤 이 시간(초)이 지나야 정지 애니메이션(IsIdle)으로 바꾼다 - 스냅샷 사이 짧은 정지로 애니메이션이 깜빡이지 않게 한다.")]
        [SerializeField, Min(0f)] private float idleDelay = 0.15f;

        private readonly SnapshotInterpolationBuffer interpolation = new();
        private Animator animator;
        private float lastMovingTime = float.NegativeInfinity;

        /// <summary>
        /// 이 원격 캐릭터가 나타내는 서버측 플레이어 id. RemotePlayerManager가 스폰 직후 SetPlayerId로 채운다.
        /// 공격 대상 판정(PlayerAttackController)에서 콜라이더로부터 대상의 id를 즉시 얻는 데 쓴다.
        /// </summary>
        public long PlayerId { get; private set; }

        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");

        private void Awake()
        {
            interpolation.Reset(transform.position, transform.eulerAngles.y);
            animator = GetComponent<Animator>();
        }

        private void Update()
        {
            interpolation.Sample(ServerClock.NowMs - SnapshotInterpolationBuffer.InterpolationDelayMs, out Vector3 position, out float rotationY);

            float speed = Time.deltaTime > 0f ? Vector3.Distance(transform.position, position) / Time.deltaTime : 0f;
            if (speed > moveSpeedThreshold)
            {
                lastMovingTime = Time.time;
            }

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));

            if (animator != null)
            {
                bool isMoving = Time.time - lastMovingTime < idleDelay;
                animator.SetBool(IsMoveHash, isMoving);
                animator.SetBool(IsIdleHash, !isMoving);
            }
        }

        /// <summary>
        /// GameServerConnectManager.OnWorldSnapshot(Game_WorldSnapshot)으로 받은 위치를 서버 시각과 함께 보간 버퍼에 넣는다.
        /// </summary>
        public void AddSnapshot(double serverTimeMs, Vector3 position, float rotationY)
        {
            interpolation.Add(serverTimeMs, position, rotationY);
        }

        /// <summary>
        /// 스폰(Game_PlayerJoined)/부활처럼 보간 없이 즉시 해당 위치/회전으로 배치한다.
        /// </summary>
        public void Warp(Vector3 position, float rotationY)
        {
            interpolation.Reset(position, rotationY);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
        }

        public void SetPlayerId(long playerId)
        {
            PlayerId = playerId;
        }
    }
}
