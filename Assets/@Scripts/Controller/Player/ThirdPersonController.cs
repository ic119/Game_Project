using System;
using UnityEngine;

namespace JJORY.Controller.Character
{
    [Serializable]
    public class CharacterMoveOptions
    {
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float sprintMultiplier = 1.5f;
        [SerializeField] private bool rotateToMove = true;
        [SerializeField] private float rotationSharpness = 12f;

        public float MoveSpeed => moveSpeed;
        public float SprintMultiplier => sprintMultiplier;
        public bool RotateToMove => rotateToMove;
        public float RotationSharpness => rotationSharpness;
    }

    [Serializable]
    public class CharacterGravityOptions
    {
        [SerializeField] private float gravityPower = -20f;
        [SerializeField] private float groundedStick = -2f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private bool enableJump = false;

        public float GravityPower => gravityPower;
        public float GroundStick => groundedStick;
        public float JumpHeight => jumpHeight;
        public bool EnableJump => enableJump;
    }

    [RequireComponent(typeof(CharacterController))]
    public class ThirdPersonController : MonoBehaviour
    {
        #region Variable
        [Header("참조 컴포넌트")]
        [SerializeField] private CharacterController characterController;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
        }

        private void Update()
        {
            
        }
        #endregion

        #region Method
        private void Move()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputValue = new Vector3(h, 0.0f, v);
        }
        #endregion
    }
}