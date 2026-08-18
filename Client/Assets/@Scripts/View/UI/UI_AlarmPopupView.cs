using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    public class UI_AlarmPopupView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private TextMeshProUGUI alarmTitleText;
        [SerializeField] private TextMeshProUGUI alarmContentText;
        #endregion

        #region LifeCycle
        #endregion

        #region Method
        public void SetAlarmText(string _title, string _content)
        {
            if (alarmTitleText != null)
            {
                alarmTitleText.text = _title;
            }

            if (alarmContentText != null)
            {
                alarmContentText.text = _content;
            }
        }
        #endregion
    }
}
