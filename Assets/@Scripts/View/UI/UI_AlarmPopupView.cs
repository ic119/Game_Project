using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public class UI_AlarmPopupView : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TextMeshProUGUI title_Text;
        [SerializeField] private TextMeshProUGUI content_Text;
        #endregion

        #region Method
        public void ContentGenerate(string _title, string _content)
        {
            title_Text.text = _title;
            content_Text.text = _content;
        }
        #endregion
    }
}