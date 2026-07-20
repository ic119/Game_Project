using System;
using DG.Tweening;
using UnityEngine;

namespace JJORY.Module
{
    public class InvertMaskController : MonoBehaviour
    {
        #region Variable
        [Header("변수")]
        [SerializeField] private Vector2 maxMaskSize = new Vector2(2200.0f, 2800.0f);

        private RectTransform rectTransform;
        private Tween maskTween;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        private void OnDestroy()
        {
            KillMaskTween();
        }
        #endregion

        #region Method
        /// <summary>
        /// 마스크 닫기 트윈 재생. 완료 후 onComplete 콜백 호출.
        /// </summary>
        public void DoClose(float _duration, Action onComplete = null)
        {
            KillMaskTween();
            maskTween = rectTransform.DOSizeDelta(Vector2.zero, _duration)
                                     .SetEase(Ease.Linear)
                                     .SetUpdate(true)
                                     .OnComplete(() =>
                                     {
                                         maskTween = null;
                                         gameObject.SetActive(false);
                                         onComplete?.Invoke();
                                     });
        }

        public void DoOpen(float _duration)
        {
            KillMaskTween();
            maskTween = rectTransform.DOSizeDelta(maxMaskSize, _duration)
                                     .SetEase(Ease.Linear)
                                     .SetUpdate(true)
                                     .OnComplete(() =>
                                     {
                                         maskTween = null;
                                         gameObject.SetActive(false);
                                     });
        }

        private void KillMaskTween()
        {
            if (maskTween == null || maskTween.IsActive() == false)
            {
                return;
            }

            maskTween.Kill();
            maskTween = null;
        }
        #endregion
    }
}