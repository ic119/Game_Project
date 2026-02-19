using UnityEngine;

namespace JJORY.Controller.Player
{
    public class PlayerAnimationController : MonoBehaviour
    {
        #region Variable
        [Header("Animation 관련 변수")]
        private string moveParameter = "MoveState";
        [SerializeField] private Animator animator;
        private int moveStatehash;
        private PlayerMoveState currentState = PlayerMoveState.Idle;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            moveStatehash = Animator.StringToHash(moveParameter);
            SetMoveState(PlayerMoveState.Idle);
        }
        #endregion

        #region Method

        /// <summary>
        /// PlayerMoveState 값에 따라 해당 애니메이션 호출
        /// </summary>
        /// <param name="_state"></param>
        public void SetMoveState(PlayerMoveState _state)
        {
            if (currentState == _state)
            {
                return;
            }
            currentState = _state;
            animator.SetInteger(moveStatehash, (int)_state);
        }
        #endregion
    }
}
