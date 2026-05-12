using System.Collections;
using UnityEngine;

public class PlayerAttackController : MonoBehaviour
{
    [SerializeField] private AnimatorController animatorController;

    [Tooltip("공격 애니메이션 재생 후 AttackState를 Idle로 되돌리기까지 대기 시간(초)")]
    [SerializeField] private float attackIdleResetDelay = 1f;

    private Coroutine attackToIdleRoutine;

    private void Awake()
    {
        if (animatorController == null)
        {
            animatorController = GetComponent<AnimatorController>();
        }
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.C))
        {
            return;
        }

        if (animatorController == null)
        {
            return;
        }

        if (attackToIdleRoutine != null)
        {
            StopCoroutine(attackToIdleRoutine);
        }

        animatorController.PlayAttackOneShot(AnimatorController.AttackAnimationType.Attack01);
        attackToIdleRoutine = StartCoroutine(CoAttackThenIdle());
    }

    private IEnumerator CoAttackThenIdle()
    {
        if (attackIdleResetDelay > 0f)
        {
            yield return new WaitForSeconds(attackIdleResetDelay);
        }

        animatorController.StopAttackAnimation();
        attackToIdleRoutine = null;
    }
}
