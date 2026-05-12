using System.Collections;
using UnityEngine;

public class AnimatorController : MonoBehaviour
{
    #region Variable
    public enum MoveAnimationType
    {
        Idle,
        Walk,
        Dash
    }

    public enum AttackAnimationType
    {
        Idle,
        Attack01,
        Attack02,
        Attack03,
        Attack04
    }

    [Header("Animator 변수")]
    [SerializeField] private Animator animator;

    [Header("Move Animation 관련 변수")]
    private string moveParameter = "MoveState";
    private int moveStateHash;
    private MoveAnimationType currentMoveState = MoveAnimationType.Idle;

    [Header("Attack Animation 관련 변수")]
    private string attackParameter = "AttackState";
    private int attackStateHash;
    private AttackAnimationType currentAttackState = AttackAnimationType.Idle;
    private Coroutine attackRoutine;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        InitMoveState();
        InitAttackState();
    }
    #endregion

    #region Method

    #region Move Animation
    /// <summary>
    /// Move 애니메이션에 대한 초기화 처리 함수
    /// </summary>
    private void InitMoveState()
    {
        moveStateHash = Animator.StringToHash(moveParameter);

        if (animator != null && !string.IsNullOrEmpty(moveParameter))
        {
            animator.SetInteger(moveStateHash, (int)MoveAnimationType.Idle);
        }
        currentMoveState = MoveAnimationType.Idle;
    }

    /// <summary>
    /// <see cref="MoveAnimationType"/> 값에 따라 이동 애니메이션 상태 설정
    /// </summary>
    /// <param name="_state">설정할 이동 상태</param>
    public void SetMoveState(MoveAnimationType _state)
    {
        if (animator == null || string.IsNullOrEmpty(moveParameter))
        {
            return;
        }

        if (currentMoveState == _state)
        {
            return;
        }

        currentMoveState = _state;
        animator.SetInteger(moveStateHash, (int)_state);
    }

    /// <summary>
    /// 이동 애니메이션을 Idle로 전환
    /// </summary>
    public void StopMoveAnimation()
    {
        SetMoveState(MoveAnimationType.Idle);
    }

    #endregion

    #region Attack Animation
    /// <summary>
    /// Attack 애니메이션에 대한 초기화 처리 함수
    /// </summary>
    private void InitAttackState()
    {
        attackStateHash = Animator.StringToHash(attackParameter);

        if (animator != null && !string.IsNullOrEmpty(attackParameter))
        {
            animator.SetInteger(attackStateHash, (int)AttackAnimationType.Idle);
        }
        currentAttackState = AttackAnimationType.Idle;
    }

    /// <summary>
    /// <see cref="AttackAnimationType"/> 값에 따라 공격 애니메이션 파라미터 설정
    /// </summary>
    public void SetAttackState(AttackAnimationType state)
    {
        if (animator == null || string.IsNullOrEmpty(attackParameter))
        {
            return;
        }

        if (currentAttackState == state)
        {
            return;
        }

        currentAttackState = state;
        animator.SetInteger(attackStateHash, (int)state);
    }

    /// <summary>
    /// 공격 파라미터를 Idle(0)로 전환
    /// </summary>
    public void StopAttackAnimation()
    {
        SetAttackState(AttackAnimationType.Idle);
    }

    /// <summary>
    /// 동일 공격을 연속으로 넣을 때도 트랜지션이 다시 타도록 한 프레임 Idle을 끼운 뒤 공격 값을 적용하고,
    /// 일정 시간 후 자동으로 Idle 전환
    /// </summary>
    /// <param name="attackType">재생할 공격 종류</param>
    /// <param name="resetToIdleAfterSeconds">이 시간이 지나면 <see cref="AttackAnimationType.Idle"/>로 복귀</param>
    public void PlayAttackOneShot(AttackAnimationType attackType, float resetToIdleAfterSeconds)
    {
        if (animator == null || string.IsNullOrEmpty(attackParameter))
        {
            return;
        }

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }

        attackRoutine = StartCoroutine(CoPlayAttackOneShot(attackType, resetToIdleAfterSeconds));
    }

    private IEnumerator CoPlayAttackOneShot(AttackAnimationType attackType, float resetToIdleAfterSeconds)
    {
        animator.SetInteger(attackStateHash, (int)AttackAnimationType.Idle);
        currentAttackState = AttackAnimationType.Idle;
        yield return null;

        animator.SetInteger(attackStateHash, (int)attackType);
        currentAttackState = attackType;

        if (resetToIdleAfterSeconds > 0f)
        {
            yield return new WaitForSeconds(resetToIdleAfterSeconds);
            animator.SetInteger(attackStateHash, (int)AttackAnimationType.Idle);
            currentAttackState = AttackAnimationType.Idle;
        }

        attackRoutine = null;
    }
    #endregion
    #endregion
}
