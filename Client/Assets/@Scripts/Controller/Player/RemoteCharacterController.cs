using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 다른 플레이어의 캐릭터를 표현한다. 입력을 직접 받는 PlayerMoveController와 달리,
    /// GameServerConnectManager가 수신한 Game_MoveBroadcast로 전달받은 목표 위치/회전을 향해
    /// 매 프레임 보간(Lerp/Slerp)만 한다 - 네트워크 지연으로 인한 순간이동을 완화한다.
    /// </summary>
    public class RemoteCharacterController : MonoBehaviour
    {
        [Header("보간 속도")]
        [SerializeField, Min(0f)] private float positionLerpSpeed = 10f;
        [SerializeField, Min(0f)] private float rotationLerpSpeed = 10f;

        [Tooltip("목표 위치까지의 거리가 이 값 미만이면 정지 상태(IsIdle)로 간주한다.")]
        [SerializeField, Min(0f)] private float moveThreshold = 0.05f;

        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Animator animator;

        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");

        private void Awake()
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
            animator = GetComponent<Animator>();
        }

        private void Update()
        {
            bool isMoving = Vector3.Distance(transform.position, targetPosition) > moveThreshold;

            transform.position = Vector3.Lerp(transform.position, targetPosition, positionLerpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationLerpSpeed * Time.deltaTime);

            if (animator != null)
            {
                animator.SetBool(IsMoveHash, isMoving);
                animator.SetBool(IsIdleHash, !isMoving);
            }
        }

        /// <summary>
        /// GameServerConnectManager.OnPlayerMoved(Game_MoveBroadcast)로 전달받은 목표 위치/회전을 갱신한다.
        /// </summary>
        public void SetTarget(Vector3 position, float rotationY)
        {
            targetPosition = position;
            targetRotation = Quaternion.Euler(0f, rotationY, 0f);
        }

        /// <summary>
        /// 스폰 직후(Game_PlayerJoined 처리 시) 보간 없이 즉시 해당 위치/회전으로 배치한다.
        /// </summary>
        public void Warp(Vector3 position, float rotationY)
        {
            targetPosition = position;
            targetRotation = Quaternion.Euler(0f, rotationY, 0f);
            transform.position = position;
            transform.rotation = targetRotation;
        }
    }
}
