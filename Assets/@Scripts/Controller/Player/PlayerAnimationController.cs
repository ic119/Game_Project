using UnityEngine;

namespace JJORY.Controller.Player
{
    public class PlayerAnimationController : MonoBehaviour
    {
        #region Variable
        [Header("Animator 변수")]
        [SerializeField] private Animator animator;

        [Header("Move Animation 관련 변수")]
        private string moveParameter = "MoveState";
        private int moveStatehash;
        private PlayerMoveState currentMoveState = PlayerMoveState.Idle;

        [Header("Attack Animaton 관련 변수")]
        private string attackParameter = "AttackState";
        private int attackStatehash;
        private PlayerAttackState currentAttackState = PlayerAttackState.Attack01;

        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            moveStatehash = Animator.StringToHash(moveParameter);
            attackStatehash = Animator.StringToHash(attackParameter);

            SetMoveState(PlayerMoveState.Idle);
            SetAttackState(PlayerAttackState.Attack01);
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
            if (currentAttackState == _state)
            {
                return;
            }
            currentAttackState = _state;
            animator.SetInteger(attackStatehash, (int)_state);
        }
        #endregion
    }
}
