using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
	#region Variable
	[Header("Components")]
	[SerializeField] private Animator animator;
	[SerializeField] private PhysicsPlayerController playerController;

	[Header("Animation Settings")]
	[SerializeField] private float walkSpeedThreshold = 0.1f;
	[SerializeField] private float runSpeedThreshold = 2.5f;

	private PlayerMoveState currentMoveState = PlayerMoveState.Idle;
	private const string MOVE_STATE_PARAMETER = "MoveState";
	#endregion

	#region LifeCycle
	private void Awake()
	{
		if (animator == null)
		{
			animator = GetComponent<Animator>();
		}

		if (playerController == null)
		{
			playerController = GetComponent<PhysicsPlayerController>();
		}
	}

	private void Update()
	{
		UpdateMoveState();
	}
	#endregion

	#region Method
	/// <summary>
	/// 플레이어의 움직임 상태에 따라 애니메이션 상태 업데이트
	/// </summary>
	private void UpdateMoveState()
	{
		if (playerController == null || animator == null)
		{
			return;
		}

		// PhysicsPlayerController에서 현재 수평 속도 정보를 가져옴
		float speed = playerController.GetCurrentHorizontalSpeed();

		// 입력 상태 확인 (PhysicsPlayerController의 ProcessMovement 로직 참고)
		float h = Input.GetAxisRaw("Horizontal");
		float v = Input.GetAxisRaw("Vertical");
		Vector3 input = new Vector3(h, 0f, v);
		bool hasInput = input.sqrMagnitude > 0.0001f;

		// 움직임 상태 결정
		PlayerMoveState newMoveState = PlayerMoveState.Idle;

		if (hasInput)
		{
			if (speed >= runSpeedThreshold)
			{
				newMoveState = PlayerMoveState.Run;
			}
			else if (speed >= walkSpeedThreshold)
			{
				newMoveState = PlayerMoveState.Walk;
			}
			else
			{
				// 입력은 있지만 속도가 낮은 경우 (회전 중 등)
				newMoveState = PlayerMoveState.Idle;
			}
		}
		else
		{
			newMoveState = PlayerMoveState.Idle;
		}

		// 상태가 변경되었을 때만 Animator 파라미터 업데이트
		if (newMoveState != currentMoveState)
		{
			currentMoveState = newMoveState;
			animator.SetInteger(MOVE_STATE_PARAMETER, (int)currentMoveState);
		}
	}
	#endregion
}
