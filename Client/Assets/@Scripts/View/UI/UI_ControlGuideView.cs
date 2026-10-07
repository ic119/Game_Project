using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// UI_GameScene의 조작 안내. 화면 모서리에 "[H] 조작 안내" 힌트를 항상 보여주고, 토글 키로 안내 패널을 켜고 끈다.
    /// 처음 게임에 입장하면(이 기기에서 한 번도 보지 않았다면) 로딩이 끝난 뒤 패널을 한 번 자동으로 보여주고 일정 시간 뒤 닫는다.
    /// 안내 문구는 프리팹의 텍스트로 직접 적는다 - 입력 키를 바꾸면 이 문구도 함께 고쳐야 한다(PlayerMoveController/PlayerAttackController 등).
    /// </summary>
    public class UI_ControlGuideView : MonoBehaviour
    {
        #region Variable
        [Header("표시 대상")]
        [Tooltip("안내 내용을 담은 패널. 이 컴포넌트가 붙은 오브젝트 자체를 끄면 Update가 멈추므로, 켜고 끌 대상은 이 자식이다.")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("모서리에 항상 보이는 \"[H] 조작 안내\" 힌트.")]
        [SerializeField] private GameObject hintRoot;

        [Header("입력")]
        [SerializeField] private KeyCode toggleKey = KeyCode.H;

        [Header("첫 입장 자동 표시")]
        [Tooltip("처음 입장했을 때 자동으로 보여준 패널을 이 시간(초) 뒤에 닫는다. 0이면 자동으로 닫지 않는다(토글 키로 닫는다).")]
        [SerializeField, Min(0f)] private float autoHideSeconds = 10f;

        private const string SeenPlayerPrefsKey = "ControlGuide.Seen";

        private bool pendingAutoShow;
        private float autoHideAt = float.PositiveInfinity;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            SetPanelActive(false);

            if (hintRoot != null)
            {
                hintRoot.SetActive(true);
            }

            pendingAutoShow = !HasSeen();
        }

        private void Update()
        {
            // 로딩바가 떠 있는 동안에는 패널이 가려지고 자동 닫힘 시간만 흘러가므로, 로딩이 끝난 뒤에 보여준다.
            if (pendingAutoShow && !IsLoadingBarVisible())
            {
                pendingAutoShow = false;
                MarkSeen();
                SetPanelActive(true);
                autoHideAt = autoHideSeconds > 0f ? Time.unscaledTime + autoHideSeconds : float.PositiveInfinity;
            }

            // 채팅 입력 중에 "h"를 쳐서 패널이 열리고 닫히지 않도록 단축키를 읽지 않는다(InputBlocker).
            if (!InputBlocker.IsBlocked && Input.GetKeyDown(toggleKey))
            {
                Toggle();
            }

            if (Time.unscaledTime >= autoHideAt)
            {
                SetPanelActive(false);
            }
        }
        #endregion

        #region Method
        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        public void Toggle()
        {
            // 직접 연 패널은 자동으로 닫지 않는다(자동 닫힘은 첫 입장 안내에만 적용).
            pendingAutoShow = false;
            autoHideAt = float.PositiveInfinity;
            SetPanelActive(!IsOpen);
        }

        private void SetPanelActive(bool active)
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(active);
            }

            if (!active)
            {
                autoHideAt = float.PositiveInfinity;
            }
        }

        private static bool IsLoadingBarVisible()
        {
            UI_LoadingBarView loadingBar = GameManager.Instance != null ? GameManager.Instance.LoadingBarView : null;
            return loadingBar != null && loadingBar.gameObject.activeInHierarchy;
        }

        // PlayerPrefs는 기기에 저장되므로 같은 기기에서는 계정이 달라도 한 번만 자동으로 보여준다. 접근이 막힌 환경에서도
        // 안내 기능 자체는 동작해야 하므로 예외는 삼키고 "본 적 없음"으로 취급한다.
        private static bool HasSeen()
        {
            try
            {
                return PlayerPrefs.GetInt(SeenPlayerPrefsKey, 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static void MarkSeen()
        {
            try
            {
                PlayerPrefs.SetInt(SeenPlayerPrefsKey, 1);
                PlayerPrefs.Save();
            }
            catch
            {
                // 저장하지 못해도 다음 입장에 한 번 더 보일 뿐이다.
            }
        }
        #endregion
    }
}
