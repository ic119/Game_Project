using Incheol.Utils;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Presenter.UI
{
    public class UI_AlarmPopup : MonoBehaviour
    {
        #region Variable
        [SerializeField] private UI_AlarmPopupView view;
        #endregion

        #region Method
        public void SetAlarmText(string _title, string _content)
        {
            if (view == null)
            {
                DebugLogManager.GenerateErrorMessage<UI_AlarmPopup>("UI_AlarmPopupView가 연결되어 있지 않습니다.");
                return;
            }

            view.SetAlarmText(_title, _content);
        }
        #endregion
    }
}
