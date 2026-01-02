using UnityEngine;


namespace JJORY.Controller.Player
{
    public class RigidBodyController : MonoBehaviour
    {
        #region Variable
        [Header("충돌 감지 변수")]
        [SerializeField] private LayerMask targetLayer;
        [SerializeField] private bool isPush;
        [SerializeField][Range(0.5f, 5.0f)] private float strength = 1.0f;
        #endregion

        #region LifeCycle
        private void OnControllerColliderHit(ControllerColliderHit _hit)
        {
            
        }
        #endregion

        #region Method
        private void PushedRigidBody(ControllerColliderHit _hit)
        {
            Rigidbody body = _hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic)
            {
                return;
            }

            int layerMask = body.gameObject.layer;
            if ((layerMask & targetLayer.value) == 0)
            {
                return;
            }

            if (_hit.moveDirection.y < -0.3f)
            {
                return;
            }

            Vector3 pushDirection = new Vector3(_hit.moveDirection.x, 0.0f, _hit.moveDirection.z);
            body.AddForce(pushDirection *  strength, ForceMode.Impulse);
        }
        #endregion
    }
}