using UnityEngine;

namespace JJORY.Controller.Camera
{
    public class CameraController : MonoBehaviour
    {
        #region Variable
        [Header("Follow Target")]
        [SerializeField] private Transform target;

        [Header("Follow Variable")]
        [SerializeField] private Vector3 positionOffset = new Vector3(0f, 5f, -8f);
        [SerializeField] private bool useWorldSpaceOffset = true;
        [Tooltip("0 = 즉시 따라감, 값이 클수록 부드럽게 따라감")]
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.2f;
        [SerializeField, Min(0f)] private float rotationSmoothTime = 0.1f;

        [Header("Look At")]
        [SerializeField] private Vector3 lookAtOffset = Vector3.zero;
        [SerializeField] private bool lookAtTarget = true;

        private Vector3 _currentVelocity;
        private Quaternion _currentRotation;
        private Transform _cachedTransform;

        #endregion

        #region LifeCycle

        private void Awake()
        {
            _cachedTransform = transform;

            if (target != null)
            {
                _cachedTransform.position = GetDesiredPosition();
                _currentRotation = _cachedTransform.rotation;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desiredPosition = GetDesiredPosition();

            if (positionSmoothTime > 0f)
            {
                _cachedTransform.position = Vector3.SmoothDamp(_cachedTransform.position, desiredPosition, ref _currentVelocity, positionSmoothTime);
            }
            else
            {
                _cachedTransform.position = desiredPosition;
            }

            if (lookAtTarget)
            {
                Vector3 lookPoint = target.position + lookAtOffset;
                Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - _cachedTransform.position);

                if (rotationSmoothTime > 0f)
                {
                    _cachedTransform.rotation = Quaternion.Slerp(_cachedTransform.rotation, desiredRotation, Time.deltaTime / rotationSmoothTime);
                }
                else
                {
                    _cachedTransform.rotation = desiredRotation;
                }
            }
        }

        #endregion

        #region Method

        private Vector3 GetDesiredPosition()
        {
            if (target == null) return _cachedTransform.position;

            Vector3 offset = useWorldSpaceOffset ? positionOffset : target.TransformDirection(positionOffset);
            return target.position + offset;
        }

        /// <summary>
        /// 추적할 타깃 설정
        /// </summary>
        public void SetTarget(Transform _target)
        {
            target = _target;

            if (target != null && _currentVelocity != default)
            {
                _currentVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 현재 추적 중인 타깃 반환
        /// </summary>
        public Transform GetTarget() => target;

        /// <summary>
        /// 위치 오프셋 설정 (월드/로컬은 useWorldSpaceOffset 유지)
        /// </summary>
        public void SetPositionOffset(Vector3 offset)
        {
            positionOffset = offset;
        }

        /// <summary>
        /// 부드러움 시간 설정. 0이면 즉시 따라감
        /// </summary>
        public void SetSmoothTime(float positionSmooth, float rotationSmooth = -1f)
        {
            positionSmoothTime = Mathf.Max(0f, positionSmooth);

            if (rotationSmooth >= 0f)
            {
                rotationSmoothTime = Mathf.Max(0f, rotationSmooth);
            }
        }
        #endregion
    }
}
