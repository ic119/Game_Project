using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 서버(GameRoom)가 권위를 갖는 몬스터 개체 하나를 표현한다. RemoteCharacterController(원격 플레이어)와 달리
    /// 이 MVP 단계의 몬스터는 서버에서도 위치가 고정(스폰 포인트)이라 이동 보간 로직은 없다 - 스폰 시 Warp로
    /// 배치한 뒤에는 피격/사망 트리거만 재생한다. 이동 AI가 추가되면 RemoteCharacterController처럼
    /// SetTarget/Lerp를 들여오면 된다.
    /// </summary>
    public class RemoteMonsterController : MonoBehaviour
    {
        private static readonly int GetHitHash = Animator.StringToHash("Get Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");

        private Animator animator;

        /// <summary>
        /// 이 인스턴스가 나타내는 서버측 몬스터 id. RemoteMonsterManager가 스폰 직후 SetMonsterId로 채운다.
        /// 공격 대상 판정(PlayerAttackController)에서 콜라이더로부터 대상의 id를 즉시 얻는 데 쓴다.
        /// </summary>
        public long MonsterId { get; private set; }
        public string MonsterType { get; private set; }

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        public void SetMonsterId(long monsterId, string monsterType)
        {
            MonsterId = monsterId;
            MonsterType = monsterType;
        }

        /// <summary>
        /// 스폰 직후 서버가 알려준 위치/회전으로 즉시 배치한다.
        /// </summary>
        public void Warp(Vector3 position, float rotationY)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
        }

        /// <summary>
        /// Game_MonsterDamageBroadcast 수신 시(사망 여부와 무관하게) 재생한다.
        /// </summary>
        public void PlayHitReaction()
        {
            if (animator != null)
            {
                animator.SetTrigger(GetHitHash);
            }
        }

        /// <summary>
        /// Game_MonsterDieBroadcast 수신 시 재생한다. 실제 오브젝트 제거는 RemoteMonsterManager가
        /// 애니메이션이 보일 시간을 준 뒤(DieAnimationDuration) 처리한다.
        /// </summary>
        public void PlayDeath()
        {
            if (animator != null)
            {
                animator.SetTrigger(DieHash);
            }
        }
    }
}
