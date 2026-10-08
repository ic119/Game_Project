using System;
using System.Collections.Generic;
using DG.Tweening;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 던전 문 여닫이 전용 컨트롤러. 근접 감지/키 입력은 UI_InteractionPrompt가 담당하고, 이 컨트롤러는 그 결과(OnInteract)를
    /// 받아 doors 목록의 모든 문짝을 DOTween으로 동시에 돌려 열거나 닫는다(양쪽으로 열리는 여닫이문처럼 문짝이 여럿인 경우를 위해 목록으로 둔다).
    /// 상호작용 키를 누를 때마다 열림/닫힘이 번갈아 바뀌고, 프롬프트 문구도 "열기"/"닫기"로 바뀐다. 닫을 때는 열 때와 같은 시간/이징을 쓴다.
    /// 문짝마다 열림 각도/걸리는 시간/이징을 따로 지정한다.
    /// 문의 상태는 각 클라이언트가 자기 로컬에서만 정하고 서버와 동기화하지 않는다 - 내가 열고 닫은 문은 내 화면에만 반영되고, 다른 플레이어의
    /// 문 상태에는 영향을 주지도 받지도 않는다. 맵을 다시 불러오면(문이 맵 프리팹에 들어 있다) 닫힌 상태로 시작한다.
    /// 각 문짝의 피벗이 경첩 위치에 있어야 자연스럽게 열린다 - 피벗이 문 중앙이면 경첩용 빈 오브젝트를 부모로 두고 그걸 doorTransform에 연결한다.
    /// </summary>
    [RequireComponent(typeof(UI_InteractionPrompt))]
    public class DoorInteractionController : MonoBehaviour
    {
        // 닫혀 있을 때/열려 있을 때 프롬프트에 붙는 행동 문구([F] 열기 / [F] 닫기).
        private const string OpenActionLabel = "열기";
        private const string CloseActionLabel = "닫기";

        /// <summary>
        /// 문짝 하나의 설정과 런타임 상태. 열림 각도는 "로컬 회전 Y의 절대값"이고, 닫힘 각도는 Awake 시점의 로컬 회전 Y다.
        /// </summary>
        [Serializable]
        private class DoorEntry
        {
            [Tooltip("회전시킬 문짝(경첩 피벗) Transform")]
            public Transform doorTransform;

            [Tooltip("문이 열렸을 때의 로컬 회전 Y값(도, 절대값). 현재(닫힌) 각도에서 가까운 방향으로 돈다.")]
            public float openRotationY = 90f;

            [Tooltip("문이 열리고 닫히는 데 걸리는 시간(초)")]
            [Min(0.05f)] public float openDuration = 0.8f;

            [Tooltip("문이 열리고 닫힐 때 트윈 이징")]
            public Ease openEase = Ease.OutCubic;

            [NonSerialized] public Vector3 closedEuler;
            [NonSerialized] public float openY;

            // 지금 문짝의 로컬 회전 Y(트윈이 갱신한다). 열리는/닫히는 도중에 다시 눌러도 현재 각도에서 이어서 돌게 한다.
            [NonSerialized] public float currentY;
        }

        [Header("문짝 목록")]
        [SerializeField] private List<DoorEntry> doors = new List<DoorEntry>();

        private UI_InteractionPrompt interactionPrompt;
        private bool isOpen;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            interactionPrompt = GetComponent<UI_InteractionPrompt>();

            foreach (DoorEntry door in doors)
            {
                if (door.doorTransform == null)
                {
                    continue;
                }

                door.closedEuler = door.doorTransform.localEulerAngles;

                // localEulerAngles는 0~360으로 나오므로(-90 == 270), 단순 선형 보간하면 가까운 길 대신 반대쪽으로 크게 돌 수 있다.
                // DeltaAngle로 닫힘 각도에서 열림 각도까지의 최단 회전량을 구해 항상 짧은 쪽으로 돌게 한다.
                door.openY = door.closedEuler.y + Mathf.DeltaAngle(door.closedEuler.y, door.openRotationY);
                door.currentY = door.closedEuler.y;
            }
        }

        // 프롬프트의 Awake가 기본 문구를 넣은 뒤에 우리 문구로 덮어쓰도록 Start에서 정한다(컴포넌트 Awake 순서에 의존하지 않는다).
        private void Start()
        {
            RefreshActionLabel();
        }

        private void OnEnable()
        {
            interactionPrompt.OnInteract += Toggle;
        }

        private void OnDisable()
        {
            interactionPrompt.OnInteract -= Toggle;
        }

        private void OnDestroy()
        {
            foreach (DoorEntry door in doors)
            {
                if (door.doorTransform != null)
                {
                    door.doorTransform.DOKill();
                }
            }
        }

        /// <summary>
        /// 열려 있으면 닫고, 닫혀 있으면 연다.
        /// </summary>
        public void Toggle()
        {
            if (isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        /// 문이 닫혀 있을 때만 모든 문짝을 연다.
        /// </summary>
        public void Open()
        {
            if (isOpen)
            {
                return;
            }

            isOpen = true;
            MoveDoors(open: true);
            RefreshActionLabel();
        }

        /// <summary>
        /// 문이 열려 있을 때만 모든 문짝을 닫는다.
        /// </summary>
        public void Close()
        {
            if (!isOpen)
            {
                return;
            }

            isOpen = false;
            MoveDoors(open: false);
            RefreshActionLabel();
        }

        // 지금 상태에 맞는 행동 문구로 바꾼다. 플레이어가 문 앞에 서 있으면 화면의 프롬프트 문구도 바로 바뀐다.
        private void RefreshActionLabel()
        {
            interactionPrompt.SetActionLabel(isOpen ? CloseActionLabel : OpenActionLabel);
        }

        private void MoveDoors(bool open)
        {
            foreach (DoorEntry door in doors)
            {
                if (door.doorTransform != null)
                {
                    PlayMove(door, open ? door.openY : door.closedEuler.y);
                }
            }
        }

        private static void PlayMove(DoorEntry door, float targetY)
        {
            Transform target = door.doorTransform;

            // 열리거나 닫히는 도중이면 멈추고 현재 각도에서 이어서 돈다. localEulerAngles를 다시 읽으면 0~360으로 정규화되므로
            // Y각은 따로 들고(currentY) 트윈한다.
            target.DOKill();
            DOTween.To(() => door.currentY, value =>
                {
                    door.currentY = value;
                    target.localEulerAngles = new Vector3(door.closedEuler.x, value, door.closedEuler.z);
                }, targetY, door.openDuration)
                .SetEase(door.openEase)
                .SetTarget(target)
                .SetLink(target.gameObject);
        }
    }
}
