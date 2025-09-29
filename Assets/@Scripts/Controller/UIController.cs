    using JJORY.Module;
using JJORY.Util;
using UnityEngine;

public class UIController : SingletonObject<UIController>
{
    #region Variable
    [Header("변수")]
    [SerializeField] private InvertMaskController invertMaskController;
    #endregion

    #region Method
    public void CloseMask()
    {
        invertMaskController.gameObject.SetActive(true);
        invertMaskController.DoClose(1.1f);
    }

    public void OpenMask()
    {
        invertMaskController.gameObject.SetActive(true);
        invertMaskController.DoOpen(1.0f);
    }
    #endregion
}
