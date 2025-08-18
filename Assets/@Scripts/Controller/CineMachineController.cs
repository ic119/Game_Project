using JJORY.Util;
using Unity.Cinemachine;
using UnityEngine;

namespace JJORY.Module
{
    public class CineMachineController : SingletonObject<CineMachineController>
    {
        #region Variable
        [Header("Target Variable")]
        [SerializeField] private Transform follow_Target;
        [SerializeField] private Transform lookAt_Target;

        [Header("Distance Variable")]
        [SerializeField] private float distance = 7.0f;
        [SerializeField] private float delay = 0.5f;

        [Header("POV Variable")]
        [SerializeField] private float hSpeed = 220.0f;
        [SerializeField] private float vSpeed = 180.0f;
        [SerializeField] private Vector2 vClamp = new Vector2(-30.0f, 60.0f);

        [Header("面倒 & 力茄 贸府")]
        [SerializeField] private bool isCollider = true;
        [SerializeField] private bool isConfiner = false;
        [SerializeField] private Collider confinerVolume;

        private CinemachineCamera cine_Cam;
        #endregion

        #region LifeCycle
        private void Awake()
        {
        }
        #endregion

        #region Method
        private void SetUp()
        {
            Camera camera = Camera.main;
            if (camera && !camera.TryGetComponent<CinemachineCamera>(out _))
            {
                camera.gameObject.AddComponent<CinemachineBrain>();
            }
        }
        #endregion
    }
}