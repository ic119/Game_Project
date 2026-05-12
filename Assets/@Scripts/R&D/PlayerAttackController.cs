using UnityEngine;

public class PlayerAttackController : MonoBehaviour
{
    private const int MaxCombo = 5;

    #region Variable
    [SerializeField] private AnimatorController animatorController;

    [Tooltip("C키 공격 입력이 다시 허용되기까지 최소 간격(초)")]
    [SerializeField] private float attackCooldown = 0.45f;

    [Tooltip("AttackState를 Idle로 되돌리기 전 대기 시간")]
    [SerializeField] private float attackStateResetDelay = 1.0f;

    [Tooltip("다음 공격 입력이 허용된 뒤 이 시간(초) 안에 C키를 누르면 다음 콤보로 진행 및 초과 시 1콤보부터 다시 시작")]
    [SerializeField] private float comboInputTime = 0.5f;

    private float nextAttackTime;
    private float lastComboInputTime = float.NegativeInfinity;
    private int comboIndex;
    #endregion

    #region LifeCycle
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

        if (Time.time < nextAttackTime)
        {
            return;
        }
        
        float comboChainExpireTime = lastComboInputTime + attackCooldown + comboInputTime;

        if (Time.time > comboChainExpireTime)
        {
            comboIndex = 0;
        }
        else
        {
            comboIndex = (comboIndex + 1) % MaxCombo;
        }

        AnimatorController.AttackAnimationType attackType = (AnimatorController.AttackAnimationType)((int)AnimatorController.AttackAnimationType.Attack01 + comboIndex);

        nextAttackTime = Time.time + attackCooldown;
        lastComboInputTime = Time.time;

        animatorController.PlayAttackOneShot(attackType, attackStateResetDelay);
    }
    #endregion
}
