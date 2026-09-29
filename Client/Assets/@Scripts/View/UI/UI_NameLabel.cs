using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// 캐릭터 머리 위에 위치하여 닉네임을 표시하는 UI 컴포넌트. 카메라를 향한 빌보드 회전은
    /// BillboardRotator(공용 컴포넌트)가 담당한다.
    /// </summary>
    [RequireComponent(typeof(BillboardRotator))]
    public class UI_NameLabel : MonoBehaviour
    {
        #region Variable
        [Header("UI Reference")]
        [SerializeField] private TextMeshProUGUI nicknameText;

        private BillboardRotator billboardRotator;
        private string currentNickname = string.Empty;
        #endregion

        #region Property
        public string Nickname => currentNickname;
        public Camera TargetCamera => billboardRotator.TargetCamera;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (nicknameText == null)
            {
                nicknameText = GetComponentInChildren<TextMeshProUGUI>(true);
            }

            billboardRotator = GetComponent<BillboardRotator>();
        }
        #endregion

        #region Method
        /// <summary>
        /// 캐릭터의 닉네임을 설정하고 텍스트를 갱신한다.
        /// </summary>
        public void SetNickname(string nickname)
        {
            currentNickname = nickname ?? string.Empty;

            if (nicknameText != null)
            {
                nicknameText.text = currentNickname;
            }
        }

        /// <summary>
        /// 빌보드가 바라볼 대상을 특정 카메라로 지정한다 (예: 프리뷰 카메라 등). BillboardRotator.SetTargetCamera로 위임한다.
        /// </summary>
        public void SetTargetCamera(Camera camera)
        {
            billboardRotator.SetTargetCamera(camera);
        }
        #endregion
    }
}
