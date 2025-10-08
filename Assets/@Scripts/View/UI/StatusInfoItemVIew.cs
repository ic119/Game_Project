using TMPro;
using UnityEngine;

namespace JJORY.View.UI
{
    public class StatusInfoItemVIew : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private TextMeshProUGUI status_Tilte;
        [SerializeField] private TextMeshProUGUI status_Value;
        #endregion

        #region Method
        /// <summary>
        /// 해당 Statue에 대한 정보값 세팅 
        /// </summary>
        public void DataSetting(string _title, int _value)
        {
            status_Tilte.text = _title;
            status_Value.text = _value.ToString();
        }
        #endregion
    }
}