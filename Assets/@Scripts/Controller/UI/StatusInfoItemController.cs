using JJORY.View.UI;
using UnityEngine;


namespace JJORY.Controller.UI
{
    public class StatusInfoItemController : MonoBehaviour
    {
        #region Variable
        [Header("View º¯¼ö")]
        [SerializeField] private StatusInfoItemVIew view;
        #endregion


        #region LifeCycle
        private void Start()
        {
            if (view == null)
            {
                view = gameObject.GetComponent<StatusInfoItemVIew>();
            }
        }
        #endregion

        #region Method
        
        #endregion
    }
}