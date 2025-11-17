using JJORY.Module;
using JJORY.Util;
using UnityEngine;
using UnityEngine.UI;


namespace JJORY.Controller.UI
{
    public class UI_GenerateCharacterPopupController : MonoBehaviour
    {
        #region Variable
        [Header("UI 변수")]
        [SerializeField] private Button check_Button;
        #endregion

        #region LifeCycle
        private void OnEnable()
        {
            if (check_Button != null)
            {
                check_Button.onClick.AddListener(OnClickedCheckButton);
            }
        }

        private void OnDisable()
        {
            if (check_Button != null)
            {
                check_Button.onClick.RemoveListener(OnClickedCheckButton);
            }
        }
        #endregion

        #region Method
        private void OnClickedCheckButton()
        {
            if (GameManager.Instance != null)
            {
                if(GameManager.Instance.isUserData == false)
                {
                    if(EventController.Instance != null)
                    {
                        EventController.Instance.InvokeGenerateCharacterPopup();
                    }
                }
            }
            gameObject.SetActive(false);

            Utils.CreateLogMessage<UI_GenerateCharacterPopupController>("캐릭터 생성 팝업 내 확인버튼 클릭");
        }
        #endregion
    }
}
