using UnityEngine;

namespace JJORY.Controller.Player
{
    public class PlayerInputController : MonoBehaviour
    {
        #region Variable
        [Header("참조 컴포넌트")]
        [SerializeField] private PlayerAnimationController animationController;

        [Header("콤보 상태")]
        [SerializeField] private int comboIndex = 0;        // 인스펙터에서 현재 몇 타까지 갔는지 확인용
        [SerializeField] private bool isAttacking = false;  // 현재 공격 중인지
        [SerializeField] private bool comboExist = false;   // 다음 콤보 입력이 들어왔는지
        [SerializeField] private bool comboEnable = false;  // 현재 프레임이 콤보 입력 가능 구간인지
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (animationController == null)
            {
                animationController = GetComponent<PlayerAnimationController>();
            }

            // 시작 시에는 Idle 상태 유지
            if (animationController != null)
            {
                animationController.ResetAttack();
            }
        }

        private void Update()
        {
            UpdateAttacking();
        }
        #endregion

        #region Method
        /// <summary>
        /// 공격 입력(X 키) 처리 및 콤보 진행 (블로그 로직 기반)
        /// </summary>
        private void UpdateAttacking()
        {
            if (!Input.GetKeyDown(KeyCode.X))
            {
                return;
            }

            if (animationController == null)
            {
                return;
            }

            // 콤보 입력 가능 구간일 때는 다음 콤보 예약만 걸어준다.
            if (comboEnable)
            {
                comboEnable = false;
                comboExist = true;
                return;
            }

            // 이미 공격 중이면 입력 무시
            if (isAttacking)
            {
                return;
            }

            // 첫 공격 시작
            isAttacking = true;
            comboIndex = 0;
            animationController.SetIsAttacking(true);
        }

        /// <summary>
        /// 애니메이션 이벤트: 콤보 입력 가능 구간 시작
        /// </summary>
        public void Combo_Enable()
        {
            comboEnable = true;
        }

        /// <summary>
        /// 애니메이션 이벤트: 콤보 입력 가능 구간 종료
        /// </summary>
        public void Combo_Disable()
        {
            comboEnable = false;
        }

        /// <summary>
        /// 애니메이션 이벤트: 다음 콤보로 넘어갈지 여부 결정
        /// </summary>
        public void Combo_Exist()
        {
            if (!comboExist)
            {
                return;
            }

            comboExist = false;
            comboIndex++;
            animationController.TriggerNextCombo();
        }

        /// <summary>
        /// 애니메이션 이벤트: 모든 공격 종료 (Idle 복귀)
        /// </summary>
        public void End_Attack()
        {
            isAttacking = false;
            comboExist = false;
            comboEnable = false;
            comboIndex = 0;

            if (animationController != null)
            {
                animationController.SetIsAttacking(false);
                animationController.ResetAttack();
            }
        }
        #endregion
    }
}
