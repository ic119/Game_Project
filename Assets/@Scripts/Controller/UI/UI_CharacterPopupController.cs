using JJORY.Module;
using JJORY.Scene;
using JJORY.Util;
using JJORY.View.UI;
using KinematicCharacterController.Examples;
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
        [SerializeField] private Button restart_Button;
        [SerializeField] private Button play_Button;

        [Header("관련 팝업창 변수")]
        [SerializeField] private GameObject ui_GenerateCharacterPopup;
        #endregion

        #region LifeCycle
        private void Start()
        {
            if (EventController.Instance != null)
            {
                EventController.Instance.OnRequestGenerateCharacterPopup += CreateCharacterData;
            }

            CreateStatusInfo();
        }

        private void OnEnable()
        {
            restart_Button.onClick.AddListener(OnClickedRestartButton);
            play_Button.onClick.AddListener(OnClickedPlayButton);
        }

        private void OnDisable()
        {
            restart_Button.onClick.RemoveListener(OnClickedRestartButton);
            play_Button.onClick.RemoveListener(OnClickedPlayButton);

            if (EventController.Instance != null)
            {
                EventController.Instance.OnRequestGenerateCharacterPopup -= CreateCharacterData;
            }

            if (GameManager.Instance != null && AddressableController.Instance != null)
            {
                GameManager.Instance.player = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.Player_Male.ToString());
                GameManager.Instance.player.transform.position = GameManager.Instance.spawn_Position.transform.position;
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 캐릭터 생성 및 불러오기 시 해당 캐릭터 스테이터스 목록 출력
        /// </summary>
        private void CreateStatusInfo() //string _title, int _value
        {
            if (statusInfoListItem_Prefab == null)
            {
                Utils.CreateLogMessage<UI_CharacterPopupController>("캐릭터 스테이터스 정보창 Prefab Null");
            }
            else
            {   
                if (GameManager.Instance != null)
                {
                    if (GameManager.Instance.isUserData == true)
                    {
                        
                    }
                    else
                    {
                        Utils.CreateLogMessage<UI_CharacterPopupController>("저장된 캐릭터 정보 없음");
                        if (ui_GenerateCharacterPopup.activeSelf == false)
                        {
                            ui_GenerateCharacterPopup.SetActive(true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 캐릭터 기본 스테이터스 생성
        /// </summary>
        private void CreateCharacterData()
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == 0)
                {
                    GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.StatusInfoItem.ToString(),content_Rect);
                    StatusInfoItemVIew view = popup.GetComponent<StatusInfoItemVIew>();
                    view.DataSetting("레벨", 1);
                }
                else if (i == 1)
                {
                    GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.StatusInfoItem.ToString(),content_Rect);
                    StatusInfoItemVIew view = popup.GetComponent<StatusInfoItemVIew>();
                    view.DataSetting("힘", 10);
                }
                else if (i == 2)
                {
                    GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.StatusInfoItem.ToString(),content_Rect);
                    StatusInfoItemVIew view = popup.GetComponent<StatusInfoItemVIew>();
                    view.DataSetting("민첩", 10);
                }
                else if (i == 3)
                {
                    GameObject popup = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(AddressKey.StatusInfoItem.ToString(),content_Rect);
                    StatusInfoItemVIew view = popup.GetComponent<StatusInfoItemVIew>();
                    view.DataSetting("지력", 10);
                }
            }
        }

        /// <summary>
        /// 생성하기 버튼 클릭 이벤트
        /// </summary>
        private void OnClickedRestartButton()
        {
            Utils.CreateLogMessage<UI_CharacterPopupController>("새로하기 버튼 클릭!");
        }

        /// <summary>
        /// 시작하기 버튼 클릭 이벤트
        /// </summary>
        private void OnClickedPlayButton()
        {
            gameObject.SetActive(false);
            Utils.CreateLogMessage<UI_CharacterPopupController>("시작하기 버튼 클릭!");
        }
        #endregion
    }
}