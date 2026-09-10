using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 키보드 화살표로만 캐릭터를 이동/회전시킨다. Rigidbody를 통해 중력의 영향을 받아 지면에 착지하고,
    /// CapsuleCollider로 맵 오브젝트와 충돌 처리된다. 둘 다 이 스크립트가 추가되는 시점에 RequireComponent로 함께 생성된다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerMoveController : MonoBehaviour
    {
        #region Variable
        [Header("Move")]
        [SerializeField, Min(0f)] private float moveSpeed = 3f;
        [SerializeField, Min(0f)] private float rotateSpeed = 120f;

        private Rigidbody rigidBody;
        private Animator animator;

        private float moveInput;
        private float turnInput;

        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
        #endregion

        #region LifeCycle
        private void Awake()
        {
            rigidBody = GetComponent<Rigidbody>();
            rigidBody.useGravity = true;
            // 이동/회전은 이 스크립트가 직접 제어하므로, 충돌로 캐릭터가 넘어지지 않도록 X/Z 회전만 고정한다(Y는 회전 제어에 필요).
            rigidBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rigidBody.interpolation = RigidbodyInterpolation.Interpolate;

            CapsuleCollider capsuleCollider = GetComponent<CapsuleCollider>();
            capsuleCollider.height = 1.8f;
            capsuleCollider.radius = 0.4f;
            capsuleCollider.center = new Vector3(0f, 0.9f, 0f);

            animator = GetComponent<Animator>();
            if (animator != null)
            {
                // 애니메이션에 포함된 루트 모션과 Rigidbody 이동이 겹쳐 밀리지 않도록, 이동은 전적으로 이 스크립트가 담당한다.
                animator.applyRootMotion = false;
            }
        }

        private void Update()
        {
            moveInput = 0f;
            if (Input.GetKey(KeyCode.UpArrow))
            {
                moveInput += 1f;
            }
            if (Input.GetKey(KeyCode.DownArrow))
            {
                moveInput -= 1f;
            }

            turnInput = 0f;
            if (Input.GetKey(KeyCode.RightArrow))
            {
                turnInput += 1f;
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                turnInput -= 1f;
            }

            UpdateAnimatorState();
        }

        private void FixedUpdate()
        {
            if (turnInput != 0f)
            {
                Quaternion turnDelta = Quaternion.Euler(0f, turnInput * rotateSpeed * Time.fixedDeltaTime, 0f);
                rigidBody.MoveRotation(rigidBody.rotation * turnDelta);
            }

            if (moveInput != 0f)
            {
                Vector3 moveDelta = transform.forward * (moveInput * moveSpeed * Time.fixedDeltaTime);
                rigidBody.MovePosition(rigidBody.position + moveDelta);
            }
        }
        #endregion

        #region Method
        private void UpdateAnimatorState()
        {
            if (animator == null)
            {
                return;
            }

            bool isMoving = moveInput != 0f || turnInput != 0f;
            animator.SetBool(IsMoveHash, isMoving);
            animator.SetBool(IsIdleHash, !isMoving);
        }
        #endregion
    }
}
