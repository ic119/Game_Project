using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// UI_GameScene에 하나만 두는 공용 상호작용 안내 UI("[F] 열기" 등). 상자/문/포털이 각자 WorldSpace Canvas를 들고 있는 대신
    /// 근접한 인터랙터블이 이 UI를 켜고 끈다 - 화면 UI라서 대상 오브젝트가 어떻게 회전해 있어도 항상 정면으로 보인다.
    /// 여러 인터랙터블 범위가 겹쳐도 꼬이지 않도록 마지막으로 Show한 소유자를 기억하고, 소유자가 Hide할 때만 끈다.
    /// </summary>
    public class UI_InteractionPromptView : MonoBehaviour
    {
        [Tooltip("실제로 켜고 끌 시각 요소의 루트. 이 컴포넌트가 붙은 오브젝트와 달라야 한다(자기 자신을 끄면 OnEnable/OnDisable이 같이 돈다).")]
        [SerializeField] private GameObject root;

        [SerializeField] private TextMeshProUGUI promptText;

        private object owner;

        /// <summary>UI_GameScene이 활성화돼 있는 동안만 값이 있다. 게임 씬 밖(로비 등)에서는 null이므로 호출부는 ?.로 부른다.</summary>
        public static UI_InteractionPromptView Instance { get; private set; }

        private void OnEnable()
        {
            Instance = this;
            SetVisible(false);
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            owner = null;
        }

        /// <summary>owner가 근접했음을 알리고 문구를 표시한다. 이미 다른 소유자가 표시 중이면 가장 최근 소유자로 바뀐다.</summary>
        public void Show(object newOwner, string text)
        {
            owner = newOwner;

            if (promptText != null)
            {
                promptText.text = text;
            }

            SetVisible(true);
        }

        /// <summary>표시 중인 소유자가 owner일 때만 끈다(다른 인터랙터블이 이어받은 프롬프트를 건드리지 않는다).</summary>
        public void Hide(object oldOwner)
        {
            if (owner != oldOwner)
            {
                return;
            }

            owner = null;
            SetVisible(false);
        }

        /// <summary>현재 표시 중인 소유자가 owner이면 문구만 바꾼다.</summary>
        public void SetText(object currentOwner, string text)
        {
            if (owner == currentOwner && promptText != null)
            {
                promptText.text = text;
            }
        }

        private void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
        }
    }
}
