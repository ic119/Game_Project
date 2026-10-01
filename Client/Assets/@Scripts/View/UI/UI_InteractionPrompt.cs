using System;
using Incheol.Controller;
using Incheol.Utils;
using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// NPC/보물상자/포털 등 상호작용 가능한 오브젝트에 붙이는 공용 프롬프트. MapPortalController.OnTriggerEnter/Exit와
    /// 같은 방식(트리거 콜라이더 + PlayerMoveController 판정)으로 스스로 플레이어 근접 여부를 감지해, 근처에 들어오면
    /// 자동으로 "[F] 상호작용" 같은 문구를 띄우고 멀어지면 숨긴다. 범위 안에서 interactionKey가 눌리면 OnInteract를
    /// 발생시킨다 - 실제 상호작용 효과(문 열기, 대화 시작 등)는 이 컴포넌트의 책임이 아니라 각 인터랙터블 전용
    /// 컨트롤러(TreasureChestInteractionController 등)가 이 이벤트를 구독해서 처리한다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class UI_InteractionPrompt : MonoBehaviour
    {
        [Header("표시 대상")]
        [Tooltip("실제로 켜고 끌 시각 요소(Canvas 등)의 루트. 이 스크립트가 붙은 오브젝트 자체를 SetActive로 끄면 " +
            "트리거 콜라이더도 같이 꺼져 재진입을 감지하지 못하므로, 트리거는 항상 켜둔 채 이 자식만 켜고 끈다.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("키/행동 문구를 표시할 텍스트. SetActionLabel로 행동 설명만 런타임에 바꿀 수 있다.")]
        [SerializeField] private TextMeshProUGUI promptText;

        [Header("상호작용")]
        [Tooltip("범위 안에 있을 때 이 키를 누르면 OnInteract가 발생한다. 표시 문구([F] 등)도 이 값을 그대로 쓴다 - " +
            "실제 입력과 화면 문구가 서로 다른 값으로 어긋나는 것을 막기 위해 별도 텍스트로 관리하지 않는다.")]
        [SerializeField] private KeyCode interactionKey = KeyCode.F;

        [Tooltip("행동 설명(예: 상호작용/열기/대화하기). 비워두면 키만 표시한다.")]
        [SerializeField] private string defaultActionLabel = "상호작용";

        /// <summary>범위 안에서 interactionKey가 눌렸을 때 발생한다. 실제 효과는 구독자가 처리한다.</summary>
        public event Action OnInteract;

        private string actionLabel;

        // 트리거 판정용. 같은 콜라이더 안에 여러 자식 콜라이더가 있어도(캐릭터 모델 하위 콜라이더 등) 중복으로
        // Show/Hide가 겹쳐 호출되지 않도록, 이미 감지한 플레이어 오브젝트를 기억해둔다.
        private GameObject currentPlayerObject;

        private void Awake()
        {
            if (TryGetComponent(out Collider col))
            {
                col.isTrigger = true;
            }

            actionLabel = defaultActionLabel;
            ApplyPromptText();

            if (visualRoot != null)
            {
                visualRoot.SetActive(false);
            }
        }

        private void Update()
        {
            // 채팅 입력 중에 상호작용 키 글자를 쳐서 상호작용(상자 열기 등)이 호출되지 않도록 한다(InputBlocker).
            if (currentPlayerObject != null && !InputBlocker.IsBlocked && Input.GetKeyDown(interactionKey))
            {
                OnInteract?.Invoke();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (currentPlayerObject != null)
            {
                return;
            }

            PlayerMoveController playerMove = other.GetComponentInParent<PlayerMoveController>();
            if (playerMove == null)
            {
                return;
            }

            currentPlayerObject = playerMove.gameObject;
            Show();
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerMoveController playerMove = other.GetComponentInParent<PlayerMoveController>();
            if (playerMove == null || playerMove.gameObject != currentPlayerObject)
            {
                return;
            }

            currentPlayerObject = null;
            Hide();
        }

        /// <summary>
        /// 프롬프트를 강제로 표시한다. 근접 감지와 별개로 외부(인터랙터블 컨트롤러)에서 필요 시 직접 호출할 수 있다.
        /// </summary>
        public void Show()
        {
            if (visualRoot != null)
            {
                visualRoot.SetActive(true);
            }
        }

        /// <summary>
        /// 프롬프트를 강제로 숨긴다(예: 상호작용 쿨다운 중이거나 대화 진행 중일 때).
        /// </summary>
        public void Hide()
        {
            if (visualRoot != null)
            {
                visualRoot.SetActive(false);
            }
        }

        /// <summary>
        /// 행동 설명 문구를 지정한다(예: "대화하기", "열기"). null/빈 문자열이면 키만 표시한다.
        /// </summary>
        public void SetActionLabel(string label)
        {
            actionLabel = label;
            ApplyPromptText();
        }

        private void ApplyPromptText()
        {
            if (promptText == null)
            {
                return;
            }

            string keyLabel = interactionKey.ToString();
            promptText.text = string.IsNullOrEmpty(actionLabel) ? keyLabel : $"[{keyLabel}] {actionLabel}";
        }
    }
}
