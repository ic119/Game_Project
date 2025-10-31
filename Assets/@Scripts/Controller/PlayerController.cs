using UnityEngine;

public class PlayerController : MonoBehaviour
{ 
    #region Variable
    [Header("컴포넌트")]
    [SerializeField] private CharacterController controller;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform player_TR;

    [Header("이동 관련")]
    [SerializeField] private float move_Speed = 2.0f;
    [SerializeField] private Vector3 dir;
    private float horizontal;
    private float vertical;

    [Header("회전 관련")]
    [SerializeField] private float rotate_Speed = 450.0f;
    #endregion

    #region LifeCycle
    private void Start()
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    private void Update()
    {
        Move();
    }
    #endregion

    #region Method
    private void Move()
    {
        if (controller != null)
        {
            if (controller.isGrounded)
            {
                horizontal = Input.GetAxis("Horizontal");
                vertical = Input.GetAxis("Vertical");

                dir = new Vector3(horizontal, 0, vertical) * move_Speed;
                
                if (dir != Vector3.zero)
                {
                    Rotate(horizontal, vertical);
                    //animator.SetBool("isWalk", true);
                    //animator.SetBool("isIdle", false);
                }
                else
                {
                    //animator.SetBool("isWalk", false);
                    //animator.SetBool("isIdle", true);
                }
            }
            else
            {
            }

            //dir.y += Physics.gravity.y * Time.deltaTime;
            controller.Move(dir * Time.deltaTime);
        }
    }

    private void Rotate(float _horizontal, float _vertical)
    {
        transform.rotation = Quaternion.Euler(0, Mathf.Atan2(_horizontal, _vertical) * Mathf.Rad2Deg, 0);
    }
    #endregion
}