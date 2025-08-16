using DG.Tweening;
using JJORY.Util;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;

namespace JJORY.Module
{
    [ExecuteInEditMode]
    public class InvertMaskController : MonoBehaviour
    {
        #region Variable
        [Header("변수")]
        [Range(0, 1)][SerializeField] private float range;
        #endregion

        #region LifeCycle
        private void Update()
        {
            GetComponent<RectTransform>().sizeDelta = Vector2.Lerp(Vector2.zero, new Vector2(2500, 2500), range);
        }
        #endregion

        #region Method
        public void DoClose(float _duration)
        {
            Utils.CreateLogMessage<InvertMaskController>("DoClose Tween 시작!");
            DOTween.KillAll();
            DOTween.To(() => range, (value) => range = value, 0.0f, _duration)
                   .onComplete += () => { this.gameObject.SetActive(false); };
        }

        public void DoOpen(float _duration)
        {
            Utils.CreateLogMessage<InvertMaskController>("DoOpen Tween 시작!");
            DOTween.KillAll();
            DOTween.To(() => range, (value) => range = value, 1.0f, _duration)
                   .onComplete += () => { this.gameObject.SetActive(false); };
        }
        #endregion
    }
}