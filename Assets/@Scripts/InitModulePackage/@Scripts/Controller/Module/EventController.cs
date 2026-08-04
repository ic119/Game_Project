using ModulePackage.Utils;
using System;

namespace ModulePackage.Controller
{
    public class EventController : SingletonObject<EventController>
    {
        #region Event Variable
        public event Action<bool> OnRequestMainCameraReset;
        public event Action<bool> OnRequestDetectionEvent;
        public event Action OnRequestViewChange;
        public event Action<bool> OnRequestEngineStartEvent;
        #endregion

        #region LifeCycle
        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnRequestMainCameraReset = null;
            OnRequestDetectionEvent = null;
            OnRequestViewChange = null;
            OnRequestEngineStartEvent = null;
        }
        #endregion

        #region Event Method
        public void InvokeMaincameraReset(bool _isInit)
        {
            OnRequestMainCameraReset?.Invoke(_isInit);
        }

        public void InvokeDetectionEvent(bool _isEvent)
        {
            OnRequestDetectionEvent?.Invoke(_isEvent);
        }

        public void InvokeViewChange()
        {
            OnRequestViewChange?.Invoke();
        }

        public void InvokeEngineStartEvent(bool _isActive)
        {
            OnRequestEngineStartEvent?.Invoke(_isActive);
        }
        #endregion
    }
}
