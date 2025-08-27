using UnityEditor.Rendering;
using UnityEngine;


namespace JJORY.Module
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterController : MonoBehaviour
    {
        #region Variable
        [Header("이동 관련")]
        [SerializeField] private bool isCameraRelative = true;
        [SerializeField] private Camera camera;

        [Header("이동 속도 관련")]
        [SerializeField] private float walk_Speed = 5.0f;
        [SerializeField] private float run_Speed = 8.5f;

        [Header("가속 & 감속 관련")]
        [SerializeField] private float walk_Acceleration = 12.0f;
        [SerializeField] private float run_Acceleration = 16.0f;
        [SerializeField] private float deceleration = 14.0f;

        [Header("회전 관련")]
        [SerializeField] private bool isRotateToMoveDirection = true;
        [SerializeField] private float rotate_Speed = 720.0f;

        [Header("중력 및 지면 관련")]
        [SerializeField] private float gravity = 20.0f;
        [SerializeField] private float groundStickForce = 2.0f;
        [SerializeField] private float slope_Limit = 45.0f;
        [SerializeField] private float step_OffSet = 0.2f;

        [Header("기타 변수")]
        private CharacterController characterController;

        [Header("State 변수 관련")]
        private Vector3 h_Velocity;
        private float v_Velocity;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            characterController.slope_Limit = slope_Limit;
            characterController.step_OffSet = step_OffSet;

            if (camera == null)
            {
                camera = Camera.main;
            }
        }
        #endregion

        #region Method
        #endregion
    }
}