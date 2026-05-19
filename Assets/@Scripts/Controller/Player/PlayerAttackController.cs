using UnityEngine;

/// <summary>
/// C키 입력 시 공격 애니메이션을 재생한 뒤 Idle로 복귀합니다.
/// </summary>
public class PlayerAttackController : MonoBehaviour
{
    #region Variable
    [SerializeField] private AnimatorController animatorController;

    [Tooltip("공격 클립 길이에 맞춰 AttackState가 Idle로 돌아갈 때까지의 시간(초)")]
    [SerializeField] private float attackToIdleDelay = 0.6f;
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

        animatorController.PlayAttackOneShot(
            AnimatorController.AttackAnimationType.Attack01,
            attackToIdleDelay);
    }
    #endregion
}
