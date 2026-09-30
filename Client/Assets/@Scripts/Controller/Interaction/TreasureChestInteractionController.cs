using DG.Tweening;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.View.UI;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 보물상자 뚜껑 개폐 전용 컨트롤러. 근접 감지/키 입력은 UI_InteractionPrompt가 담당하고, 이 컨트롤러는
    /// 그 결과(OnInteract)를 받아 서버에 개봉을 요청한다 - 실제 골드/아이템 지급과 선착순 판정은 서버 권위다
    /// (GameRoom.TryOpenChest). 뚜껑 애니메이션은 요청 즉시가 아니라 서버가 확인해준(Game_ChestOpenBroadcast)
    /// 뒤에만 재생한다 - PlayerAttackController가 원격 공격 모션을 서버 브로드캐스트로만 재생하는 것과 같은 패턴이다.
    /// </summary>
    [RequireComponent(typeof(UI_InteractionPrompt))]
    public class TreasureChestInteractionController : MonoBehaviour
    {
        [Tooltip("MapData/{mapId}.json의 chests[].id와 정확히 일치해야 하는 고유 식별자. " +
            "Tools/Map/Export Map Data From Selected Prefab이 이 값을 그대로 내보낸다.")]
        [SerializeField] private string chestId;

        [Tooltip("Drops/DropTables.json에서 이 상자가 쓸 항목의 키(몬스터 타입과 같은 딕셔너리를 공유한다).")]
        [SerializeField] private string lootTableKey = "TreasureChestBasic";

        [Tooltip("회전시킬 뚜껑 Transform")]
        [SerializeField] private Transform chestLid;

        [Tooltip("뚜껑이 열릴 때 현재 로컬 회전 X값에 더해지는 각도(도). 음수/양수로 방향을 뒤집을 수 있다.")]
        [SerializeField] private float openAngle = -130f;

        [Tooltip("뚜껑이 열리는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.05f)] private float openDuration = 0.6f;

        [Tooltip("회전 트윈 이징")]
        [SerializeField] private Ease openEase = Ease.OutBack;

        [Header("사라지는 연출 (리스폰으로 제거될 때)")]
        [Tooltip("상자가 땅으로 가라앉으며 작아지는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.05f)] private float despawnDuration = 0.6f;

        [Tooltip("사라지는 동안 아래로 가라앉는 거리(m)")]
        [SerializeField, Min(0f)] private float despawnSinkDistance = 0.4f;

        [Tooltip("크기가 줄어드는 트윈 이징(InBack은 살짝 부풀었다가 줄어든다)")]
        [SerializeField] private Ease despawnEase = Ease.InBack;

        private UI_InteractionPrompt interactionPrompt;
        private bool isOpen;

        public string ChestId => chestId;

        private void Awake()
        {
            interactionPrompt = GetComponent<UI_InteractionPrompt>();
        }

        /// <summary>
        /// 런타임에 생성한 상자(RemoteChestManager - 서버가 후보에서 뽑아 알려준 상자)에 서버가 정한 id를 주입한다.
        /// 씬에 미리 배치한 상자는 인스펙터의 chestId를 그대로 쓰므로 호출하지 않는다.
        /// </summary>
        public void Initialize(string id)
        {
            chestId = id;
        }

        /// <summary>
        /// 이 상자가 이미 열려 있다는 사실을 뒤늦게 반영한다. 서버의 열림 알림(OnChestOpened)이 상자 생성보다 먼저 도착해
        /// 이 컨트롤러가 구독하기 전에 지나간 경우 RemoteChestManager가 생성 직후 호출한다.
        /// </summary>
        public void ApplyOpenedState()
        {
            OpenLid();
        }

        /// <summary>
        /// 리스폰으로 이 상자가 제거될 때의 연출: 상호작용을 즉시 끄고, 땅으로 가라앉으며 작아진 뒤 onComplete를 부른다
        /// (호출측이 그때 오브젝트를 파괴한다). 트윈은 이 GameObject에 링크되어 있어서, 연출 도중 맵 전환 등으로
        /// 오브젝트가 먼저 파괴되어도 안전하게 정리된다(그 경우 onComplete는 불리지 않는다).
        /// </summary>
        public void PlayDespawn(System.Action onComplete)
        {
            interactionPrompt.Hide();
            interactionPrompt.enabled = false; // 사라지는 도중 근접/키 입력으로 다시 개봉을 요청하지 않게 한다.

            if (TryGetComponent(out Collider triggerCollider))
            {
                triggerCollider.enabled = false;
            }

            if (chestLid != null)
            {
                chestLid.DOKill(); // 뚜껑이 아직 열리는 중이었다면 멈춘다.
            }

            transform.DOKill();
            Sequence sequence = DOTween.Sequence().SetLink(gameObject);
            sequence.Join(transform.DOScale(Vector3.zero, despawnDuration).SetEase(despawnEase));
            sequence.Join(transform.DOMoveY(transform.position.y - despawnSinkDistance, despawnDuration).SetEase(Ease.InQuad));
            sequence.OnComplete(() => onComplete?.Invoke());
        }

        private void OnEnable()
        {
            interactionPrompt.OnInteract += HandleInteract;

            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnChestOpened += HandleChestOpenedBroadcast;
            }
        }

        private void OnDisable()
        {
            interactionPrompt.OnInteract -= HandleInteract;

            if (GameServerConnectManager.Instance != null)
            {
                GameServerConnectManager.Instance.OnChestOpened -= HandleChestOpenedBroadcast;
            }
        }

        // 근접 + 키 입력(UI_InteractionPrompt.OnInteract)이 확인되면 서버에 개봉을 요청만 한다. 여기서 뚜껑을
        // 바로 열지 않는다 - 서버가 사거리/선착순을 검증한 뒤 Game_ChestOpenBroadcast로 확인해줘야 실제로 연다.
        private void HandleInteract()
        {
            if (isOpen || string.IsNullOrEmpty(chestId))
            {
                return;
            }

            GameServerConnectManager.Instance?.SendChestOpenRequest(chestId);
        }

        // 본인이 방금 요청한 경우, 다른 플레이어가 먼저 연 경우, 방에 새로 입장해 이미 열린 상자를 따라잡는
        // 경우를 전부 이 한 경로로 처리한다 - 어느 쪽이든 결과는 "이 상자는 이제 열려 있다"로 동일하다.
        private void HandleChestOpenedBroadcast(GameChestOpenBroadcastPacket packet)
        {
            if (packet.ChestId != chestId)
            {
                return;
            }

            OpenLid();
        }

        private void OpenLid()
        {
            if (isOpen || chestLid == null)
            {
                return;
            }

            isOpen = true;
            interactionPrompt.Hide();
            interactionPrompt.enabled = false; // 다시 근접해도 프롬프트가 뜨거나 재요청이 나가지 않게 트리거 자체를 끈다.

            // RotateMode.LocalAxisAdd: 현재 로컬 회전에 openAngle만큼 X축으로 더한다(절대값을 새로 지정하지 않는다).
            chestLid.DOLocalRotate(new Vector3(openAngle, 0f, 0f), openDuration, RotateMode.LocalAxisAdd)
                .SetEase(openEase);
        }
    }
}
