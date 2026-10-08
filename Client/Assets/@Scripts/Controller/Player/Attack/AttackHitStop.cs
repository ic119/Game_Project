using System.Collections;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 내 공격이 명중했을 때 공격 애니메이션을 잠깐 멈춰 타격감을 주는 히트스톱. Time.timeScale을 건드리지 않고 이 캐릭터의
    /// 애니메이터만 멈추므로 서버와 맞춰 돌아가는 보간/쿨다운/네트워크 시각에는 영향이 없다. 멈춘 시간만큼 애니메이션이 로직
    /// 타이머보다 살짝 늦어지지만 0.05초 안팎이라 눈에 띄지 않는다.
    /// </summary>
    public class AttackHitStop
    {
        private readonly MonoBehaviour owner;
        private readonly Animator animator;
        private Coroutine routine;

        public AttackHitStop(MonoBehaviour owner, Animator animator)
        {
            this.owner = owner;
            this.animator = animator;
        }

        public void Begin(float seconds)
        {
            if (seconds <= 0f || animator == null || routine != null)
            {
                return;
            }

            routine = owner.StartCoroutine(Run(seconds));
        }

        /// <summary>
        /// 히트스톱 도중 비활성화/해제되면 멈춘 채 남지 않게 속도를 1로 되돌리고 코루틴 상태를 지운다.
        /// </summary>
        public void End()
        {
            if (routine == null)
            {
                return;
            }

            owner.StopCoroutine(routine);
            routine = null;

            if (animator != null)
            {
                animator.speed = 1f;
            }
        }

        private IEnumerator Run(float seconds)
        {
            float previousSpeed = animator.speed;
            animator.speed = 0f;

            // 실제 시간으로 기다린다(타임스케일과 무관하게 같은 길이로 멈추도록).
            yield return new WaitForSecondsRealtime(seconds);

            if (animator != null)
            {
                animator.speed = previousSpeed;
            }

            routine = null;
        }
    }
}
