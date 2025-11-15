using JJORY.Util;
using System;
using UnityEngine;

namespace JJORY.Module
{
    public class EventController : SingletonObject<EventController>
    {
        #region Variable
        public event Action<string, string> OnRequestShowPopup;
        public event Action OnRequestGenerateCharacterPopup;
        #endregion

        #region LifeCycle
        private void OnDestroy()
        {
            OnRequestShowPopup = null;

            OnRequestGenerateCharacterPopup = null;
        }
        #endregion

        #region Methoe_InvokeEvent
        /// <summary>
        /// 알람 팝업창 표출 이벤트
        /// </summary>
        /// <param name="_title"></param>
        /// <param name="_content"></param>
        public void InvokeShowPopup(string _title, string _content)
        {
            OnRequestShowPopup?.Invoke(_title, _content);   
        }

        /// <summary>
        /// 캐릭터 생성 팝업창 표출 이벤트 함수
        /// </summary>
        public void InvokeGenerateCharacterPopup()
        {
            OnRequestGenerateCharacterPopup?.Invoke();
        }
        #endregion
    }
}