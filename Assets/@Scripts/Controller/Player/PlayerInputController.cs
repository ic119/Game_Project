using JJORY.Controller.Player;
using UnityEngine;


namespace JJORY.Controller.Player
{
    public class PlayerInputController : MonoBehaviour
    {
        #region Variable
        [Header("참조 컴포넌트")]
        [SerializeField] private PlayerAnimationController animationController;

        [Header("무기 / 콤보 설정")]
        [SerializeField] private PlayerWeaponType currentWeaponType = PlayerWeaponType.OneHandedSword;
        [SerializeField, Min(0.05f)] private float comboResetTime = 1.0f;

        private PlayerAttackState currentComboState = PlayerAttackState.Attack01;
        private float lastAttackTime;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (animationController == null)
            {
                animationController = GetComponent<PlayerAnimationController>();
            }
        }

        private void Update()
        {
            HandleAttackInput();
        }
        #endregion

        #region Method
        /// <summary>
        /// 공격 입력(X 키) 처리
        /// </summary>
        private void HandleAttackInput()
        {
            if (!Input.GetKeyDown(KeyCode.X))
            {
                return;
            }

            if (animationController == null)
            {
                return;
            }

            float now = Time.time;

            // 콤보 리셋 시간 초과 시 처음부터
            if (now - lastAttackTime > comboResetTime)
            {
                currentComboState = PlayerAttackState.Attack01;
            }
            else
            {
                currentComboState = GetNextAttackState(currentComboState, currentWeaponType);
            }

            lastAttackTime = now;
            animationController.SetAttackState(currentComboState);
        }

        /// <summary>
        /// 무기 타입에 따라 다음 콤보 공격 상태 반환
        /// </summary>
        private PlayerAttackState GetNextAttackState(PlayerAttackState current, PlayerWeaponType weaponType)
        {
            int maxIndex = (int)PlayerAttackState.AttackFinish; // 기본: 4타(0~3)

            switch (weaponType)
            {
                case PlayerWeaponType.Dagger:
                case PlayerWeaponType.DualDagger:
                    // 단검/쌍단검: 4타 풀 콤보 사용
                    maxIndex = (int)PlayerAttackState.AttackFinish;
                    break;

                case PlayerWeaponType.OneHandedSword:
                    // 한손검: 3타까지 사용 (Attack03까지)
                    maxIndex = (int)PlayerAttackState.Attack03;
                    break;

                case PlayerWeaponType.TwoHandedSword:
                    // 양손검: 2타까지 사용 (Attack02까지)
                    maxIndex = (int)PlayerAttackState.Attack02;
                    break;
            }

            int currentIndex = (int)current;

            if (currentIndex >= maxIndex)
            {
                return PlayerAttackState.Attack01;
            }

            return (PlayerAttackState)(currentIndex + 1);
        }
        #endregion
    }
}
