using Incheol.Modules;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 로컬 플레이어 전용. 일정 주기로 자신의 위치/회전을 GameServerConnectManager를 통해
    /// GameServer(Game_MoveRequest)로 보낸다. 직전 전송값과 거의 같으면(정지 상태) 보내지 않는다.
    /// </summary>
    public class PlayerNetworkSender : MonoBehaviour
    {
        [Tooltip("전송 주기(초). 너무 짧으면 트래픽이 늘고, 너무 길면 다른 클라이언트 화면에서 끊겨 보인다.")]
        [SerializeField, Min(0.02f)] private float sendInterval = 0.1f;

        [SerializeField, Min(0f)] private float positionChangeThreshold = 0.01f;
        [SerializeField, Min(0f)] private float rotationChangeThreshold = 0.5f;

        private float sendTimer;
        private Vector3 lastSentPosition;
        private float lastSentRotationY;
        private bool hasSentOnce;

        private void Update()
        {
            sendTimer += Time.deltaTime;
            if (sendTimer < sendInterval)
            {
                return;
            }
            sendTimer = 0f;

            float rotationY = transform.eulerAngles.y;
            bool moved = !hasSentOnce
                || Vector3.Distance(transform.position, lastSentPosition) > positionChangeThreshold
                || Mathf.Abs(Mathf.DeltaAngle(lastSentRotationY, rotationY)) > rotationChangeThreshold;

            if (!moved)
            {
                return;
            }

            lastSentPosition = transform.position;
            lastSentRotationY = rotationY;
            hasSentOnce = true;

            GameServerConnectManager.Instance?.SendMove(transform.position.x, transform.position.y, transform.position.z, rotationY);
        }
    }
}
