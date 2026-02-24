using UnityEngine;

namespace JJORY.Controller.Player
{
    public class PlayerAnimationController : MonoBehaviour
    {
        #region Variable
        [Header("Animator 변수")]
        [SerializeField] private Animator animator;

        // 공격을 하지 않는 상태를 나타내는 Animator 파라미터 값
        // Animator 내 조건에서는 0~3만 사용하고, -1은 어떤 공격 애니메이션과도 매칭되지 않도록 사용한다.
        private const int AttackNoneValue = -1;

        [Header("Move Animation 관련 변수")]
        private string moveParameter = "MoveState";
        private int moveStatehash;
        private PlayerMoveState currentMoveState = PlayerMoveState.Idle;

        [Header("Attack Animaton 관련 변수")]
        [Tooltip("기존 정수형 콤보 파라미터 (사용하지 않으면 비워둬도 됩니다)")]
        [SerializeField] private string attackParameter = "AttackState";
        private int attackStatehash;
        private PlayerAttackState currentAttackState = PlayerAttackState.Attack01;

        [Header("콤보 트리거 기반 파라미터")]
        [SerializeField] private string isAttackingParameter = "IsAttacking";
        [SerializeField] private string nextComboTriggerParameter = "NextCombo";
        private int isAttackingHash;
        private int nextComboTriggerHash;

        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            moveStatehash = Animator.StringToHash(moveParameter);

            isAttackingHash = Animator.StringToHash(isAttackingParameter);
            nextComboTriggerHash = Animator.StringToHash(nextComboTriggerParameter);

            SetMoveState(PlayerMoveState.Idle);

            // 시작 시에는 공격 애니메이션이 재생되지 않도록 초기화
            if (attackStatehash != 0)
            {
                currentAttackState = (PlayerAttackState)AttackNoneValue;
                animator.SetInteger(attackStatehash, AttackNoneValue);
            }

            animator.SetBool(isAttackingHash, false);
        }
        #endregion

        #region Method

        /// <summary>
        /// PlayerMoveState 값에 따라 해당 애니메이션 호출
        /// </summary>
        /// <param name="_state"></param>
        public void SetMoveState(PlayerMoveState _state)
        {
            if (currentMoveState == _state)
            {
                return;
            }
            currentMoveState = _state;
            animator.SetInteger(moveStatehash, (int)_state);
        }

        /// <summary>
        /// PlayerAttackState 값에 따라 해당 애니메이션 호출
        /// </summary>
        /// <param name="_state"></param>
        public void SetAttackState(PlayerAttackState _state)
        {
            if (attackStatehash == 0)
            {
                return;
            }

            if (currentAttackState == _state)
            {
                return;
            }
            currentAttackState = _state;
            animator.SetInteger(attackStatehash, (int)_state);
        }

        /// <summary>
        /// 공격 여부(IsAttacking) 파라미터 설정
        /// </summary>
        public void SetIsAttacking(bool isAttacking)
        {
            animator.SetBool(isAttackingHash, isAttacking);
        }

        /// <summary>
        /// 다음 콤보로 넘어가는 트리거(NextCombo) 발동
        /// </summary>
        public void TriggerNextCombo()
        {
            animator.SetTrigger(nextComboTriggerHash);
        }

        /// <summary>
        /// 공격 애니메이션과 파라미터를 초기 상태(Idle)로 리셋
        /// </summary>
        public void ResetAttack()
        {
            // 공격 파라미터를 "공격 없음" 값으로 돌리고, 이동 상태를 Idle로 유지한다.
            if (attackStatehash != 0)
            {
                currentAttackState = (PlayerAttackState)AttackNoneValue;
                animator.SetInteger(attackStatehash, AttackNoneValue);
            }

            animator.SetBool(isAttackingHash, false);
            SetMoveState(PlayerMoveState.Idle);
        }
        #endregion
    }
}
