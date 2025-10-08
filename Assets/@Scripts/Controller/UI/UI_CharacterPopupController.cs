using JJORY.Module;
using JJORY.Util;
using JJORY.View.UI;
using System.Runtime.ExceptionServices;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UI;

namespace JJORY.Controller.UI
{
    public class UI_CharacterPopupController : MonoBehaviour
    {
        #region Variable
        [Header("정보창 스테이터스 ScrollView 관련 변수")]
        [SerializeField] private GameObject statusInfoListItem_Prefab;
        [SerializeField] private RectTransform content_Rect;

        [Header("상호작용 버튼 변수")]
        [SerializeField] private Button create_Button;
        [SerializeField] private Button play_Button;
        #endregion

        #region LifeCycle
        private void Start()
        {
            CreateStatusInfo();
        }

        private void OnEnable()
        {
            create_Button.onClick.AddListener(OnClickedCreateButton);
            play_Button.onClick.AddListener(OnClickedPlayButton);
        }

        private void OnDisable()
        {
            create_Button.onClick.RemoveListener(OnClickedCreateButton);
            play_Button.onClick.RemoveListener(OnClickedPlayButton);
        }
        #endregion

        #region Method
        private void CreateStatusInfo() //string _title, int _value
        {
            if (statusInfoListItem_Prefab == null)
            {
                Utils.CreateLogMessage<UI_CharacterPopupController>("캐릭터 스테이터스 정보창 Prefab Null");
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.StatusInfoItem.ToString(),
                                                                                                          content_Rect);
                    StatusInfoItemVIew view = popup.GetComponent<StatusInfoItemVIew>();
                    if (i == 0)
                    {
                        view.DataSetting("레벨", 1);
                    }
                    else if (i == 1)
                    {
                        view.DataSetting("힘", 10);
                    }
                    else if (i == 2)
                    {
                        view.DataSetting("민첩", 10);
                    }
                    else if (i == 3)
                    {
                        view.DataSetting("지력", 10);
                    }
                }
                //StatusInfoItemVIew view = statusInfoListItem_Prefab.GetComponent<StatusInfoItemVIew>();
                //view.DataSetting(_title, _value);
            }
        }

        /// <summary>
        /// 생성하기 버튼 클릭 이벤트
        /// </summary>
        private void OnClickedCreateButton()
        {
            Utils.CreateLogMessage<UI_CharacterPopupController>("생성하기 버튼 클릭!");
        }

        /// <summary>
        /// 시작하기 버튼 클릭 이벤트
        /// </summary>
        private void OnClickedPlayButton()
        {
            Utils.CreateLogMessage<UI_CharacterPopupController>("시작하기 버튼 클릭!");
        }
        #endregion
    }
}