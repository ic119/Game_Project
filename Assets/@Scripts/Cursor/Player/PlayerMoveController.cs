using UnityEngine;

public class PlayerMoveController : MonoBehaviour
{
    #region Variable
    [Header("이동 관련")]
    [SerializeField] private float moveSpeed;
    private float vAxis;
    private float hAxis;
    private Vector3 moveVec;
    #endregion

    #region LifeCycle
    #endregion

    #region Method
    private void Move()
    {
        hAxis = Input.GetAxis("Horizontal");
        vAxis = Input.GetAxis("Vertical");

    }
    #endregion
}