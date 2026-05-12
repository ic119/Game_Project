using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerCharacterControllor : MonoBehaviour
{
    #region Variable
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float dashValue = 2.0f;
    [SerializeField] private float rotationDegreesPerSecond = 540f;

    [SerializeField] private AnimatorController animatorController;
    [SerializeField] private PlayerChaseCamera playerChaseCamera;

    private CharacterController characterController;
    #endregion

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (animatorController == null)
        {
            animatorController = GetComponent<AnimatorController>();
        }

        if (playerChaseCamera == null)
        {
            playerChaseCamera = GameObject.FindAnyObjectByType<PlayerChaseCamera>();
        }
        playerChaseCamera.followTarget = gameObject.transform;
    }

    private void Update()
    {
        Vector3 input = Vector3.zero;

        if (Input.GetKey(KeyCode.UpArrow))
        {
            input += Vector3.forward;
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            input += Vector3.back;
        }
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            input += Vector3.left;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            input += Vector3.right;
        }

        Move(input);
    }

    /// <summary>
    /// 입력 방향(월드 XZ)으로 이동하고, 해당 방향을 향해 회전합니다.
    /// </summary>
    public void Move(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            if (animatorController != null)
            {
                animatorController.StopMoveAnimation();
            }

            return;
        }

        direction.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                                                      targetRotation,
                                                      rotationDegreesPerSecond * Time.deltaTime);

        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (animatorController != null)
        {
            animatorController.SetMoveState(isShiftPressed
                ? AnimatorController.MoveAnimationType.Dash
                : AnimatorController.MoveAnimationType.Walk);
        }

        float currentMoveSpeed = isShiftPressed ? moveSpeed * dashValue : moveSpeed;
        
        Vector3 motion = direction * (currentMoveSpeed * Time.deltaTime);

        characterController.Move(motion);
    }
} 
