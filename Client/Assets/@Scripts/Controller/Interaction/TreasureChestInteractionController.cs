using DG.Tweening;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 보물상자 뚜껑 개폐 전용 컨트롤러. 근접 감지/키 입력은 UI_InteractionPrompt가 담당하고, 이 컨트롤러는
    /// 그 결과(OnInteract)만 구독해서 실제 효과(뚜껑 회전)를 실행한다 - MapPortalController가 트리거는 직접
    /// 갖되 텔레포트 로직만 책임지는 것과 같은 구조다.
    /// </summary>
    [RequireComponent(typeof(UI_InteractionPrompt))]
    public class TreasureChestInteractionController : MonoBehaviour
    {
        [Tooltip("회전시킬 뚜껑 Transform")]
        [SerializeField] private Transform chestLid;

        [Tooltip("뚜껑이 열릴 때 현재 로컬 회전 X값에 더해지는 각도(도). 음수/양수로 방향을 뒤집을 수 있다.")]
        [SerializeField] private float openAngle = -130f;

        [Tooltip("뚜껑이 열리는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.05f)] private float openDuration = 0.6f;

        [Tooltip("회전 트윈 이징")]
        [SerializeField] private Ease openEase = Ease.OutBack;

        private UI_InteractionPrompt interactionPrompt;
        private bool isOpen;

        private void Awake()
        {
            interactionPrompt = GetComponent<UI_InteractionPrompt>();
        }

        private void OnEnable()
        {
            interactionPrompt.OnInteract += HandleInteract;
        }

        private void OnDisable()
        {
            interactionPrompt.OnInteract -= HandleInteract;
        }

        private void HandleInteract()
        {
            if (isOpen || chestLid == null)
            {
                return;
            }

            isOpen = true;
            interactionPrompt.Hide();

            // RotateMode.LocalAxisAdd: 현재 로컬 회전에 openAngle만큼 X축으로 더한다(절대값을 새로 지정하지 않는다).
            chestLid.DOLocalRotate(new Vector3(openAngle, 0f, 0f), openDuration, RotateMode.LocalAxisAdd)
                .SetEase(openEase);
        }
    }
}
