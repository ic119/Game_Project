using Incheol.Utils;
using UnityEngine;

namespace Incheol.Modules
{
    public class EventMethodManager : SingletonObject<EventMethodManager>
    {
        #region Event Variable
        #endregion

        #region LifeCycle
        protected override void OnDestroy()
        {
            base.OnDestroy();
            //등록할 이벤트 변수 초기화 처리 구간
        }
        #endregion

        #region Event Method
        #endregion
    }
}