using TMPro;
using UnityEngine;

namespace JJORY.View.UI
{
    public class CharacterStatListItemView : MonoBehaviour
    {
        #region Variable
        [Header("UI Variable")]
        [SerializeField] private TextMeshProUGUI statValueText;
        #endregion

        #region LifeCycle
        private void Start()
        {
            if (statValueText == null)
            {
                statValueText = GetComponent<TextMeshProUGUI>();
            }
        }

        #endregion

        #region Method
        #endregion
    }
}
