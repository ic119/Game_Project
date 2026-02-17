using System;
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
    /// <summary>
    /// 마스크 닫기 트윈 재생. onComplete는 트윈이 끝난 뒤 호출된다.
    /// </summary>
    public void CloseMask(Action onComplete = null)
    {
        invertMaskController.gameObject.SetActive(true);
        invertMaskController.DoClose(1.1f, () =>
        {
            Utils.CreateLogMessage<UIController>("CloseMask 완료");
            onComplete?.Invoke();
        });
    }

    public void OpenMask()
    {
        invertMaskController.gameObject.SetActive(true);
        invertMaskController.DoOpen(1.0f);
        Utils.CreateLogMessage<UIController>("OpenMask 완료");
    }
    #endregion
}
