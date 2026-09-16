using Incheol.Modules;
using Incheol.Utils;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Incheol.Controller.Interaction
{
    public enum PortalTeleportType
    {
        SceneLoad,         // 다른 씬으로 이동 (SceneLoadManager)
        CoordinateTeleport // 같은 씬 내의 다른 위치로 순간이동
    }

    /// <summary>
    /// 맵 이동 및 순간이동을 지원하는 포털 컨트롤러 컴포넌트.
    /// 플레이어가 포털 영역에 진입했을 때 씬 전환(SceneLoadManager) 또는 좌표 이동을 처리하며,
    /// 연한 파랑 계열의 빛 펄스, 룬 링 회전, 파티클 상승 등의 시각 효과를 제어한다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MapPortalController : MonoBehaviour
    {
        #region Variable
        [Header("Teleport Settings")]
        [Tooltip("포털 동작 방식: 씬 로드 또는 좌표 텔레포트")]
        [SerializeField] private PortalTeleportType teleportType = PortalTeleportType.SceneLoad;

        [Tooltip("SceneLoad 방식일 때 이동할 목표 씬의 태그(예: GameScene, LobbyScene 등)")]
        [SerializeField] private string targetSceneTag = "LobbyScene";

        [Tooltip("CoordinateTeleport 방식일 때 이동할 목표 Transform")]
        [SerializeField] private Transform targetDestination;

        [Tooltip("CoordinateTeleport 방식일 때 목표 Transform이 없을 경우 이동할 고정 월드 좌표")]
        [SerializeField] private Vector3 targetCoordinate = Vector3.zero;

        [Tooltip("포털 진입 후 텔레포트 실행까지의 지연 시간(초)")]
        [SerializeField, Min(0f)] private float teleportDelay = 0.5f;

        [Tooltip("포털 재사용 쿨다운 시간(초)")]
        [SerializeField, Min(0.1f)] private float cooldownTime = 2f;

        [Tooltip("즉시 진입이 아닌 상호작용 키(F 등) 입력이 필요한지 여부")]
        [SerializeField] private bool requireInteractionKey = false;

        [Tooltip("상호작용 키 (requireInteractionKey가 true일 때 사용)")]
        [SerializeField] private KeyCode interactionKey = KeyCode.F;

        [Header("Visual Effects")]
        [Tooltip("포털 중앙의 포인트 라이트 (부드러운 호흡/펄스 효과용)")]
        [SerializeField] private Light portalLight;

        [Tooltip("빛 펄스 속도")]
        [SerializeField] private float lightPulseSpeed = 2f;

        [Tooltip("최소 빛 강도")]
        [SerializeField] private float minLightIntensity = 1.2f;

        [Tooltip("최대 빛 강도")]
        [SerializeField] private float maxLightIntensity = 2.8f;

        [Tooltip("지속적으로 회전시킬 룬/링 Transform 배열")]
        [SerializeField] private Transform[] rotatingRings;

        [Tooltip("각 링의 회전 속도 (Y축 기준, 양수/음수로 시계/반시계 방향)")]
        [SerializeField] private float[] ringRotationSpeeds;

        [Tooltip("플레이어 진입 시 추가로 재생할 버스트/진입 파티클 (선택 사항)")]
        [SerializeField] private ParticleSystem enterBurstEffect;

        [Header("Audio")]
        [Tooltip("포털 진입 시 재생할 오디오 클립")]
        [SerializeField] private AudioClip teleportSound;

        [Header("Events")]
        [Tooltip("텔레포트 시작 시 호출되는 이벤트")]
        public UnityEvent onTeleportStarted;

        [Tooltip("텔레포트 완료 시 호출되는 이벤트")]
        public UnityEvent onTeleportCompleted;

        private bool isTeleporting = false;
        private bool isCoolingDown = false;
        private bool isPlayerInside = false;
        private GameObject currentPlayerObject;
        private AudioSource audioSource;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null && teleportSound != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0.8f;
            }
        }

        private void Update()
        {
            UpdateVisualEffects();

            if (isPlayerInside && requireInteractionKey && !isTeleporting && !isCoolingDown)
            {
                if (Input.GetKeyDown(interactionKey))
                {
                    StartTeleportSequence(currentPlayerObject);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (isTeleporting || isCoolingDown)
            {
                return;
            }

            // 플레이어 캐릭터(PlayerMoveController 소유)인지 판별
            PlayerMoveController playerMove = other.GetComponentInParent<PlayerMoveController>();
            if (playerMove == null)
            {
                return;
            }

            isPlayerInside = true;
            currentPlayerObject = playerMove.gameObject;

            if (!requireInteractionKey)
            {
                StartTeleportSequence(currentPlayerObject);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerMoveController playerMove = other.GetComponentInParent<PlayerMoveController>();
            if (playerMove != null && playerMove.gameObject == currentPlayerObject)
            {
                isPlayerInside = false;
                currentPlayerObject = null;
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 포털의 빛 호흡 효과와 룬 링들의 부드러운 회전을 매 프레임 업데이트한다.
        /// </summary>
        private void UpdateVisualEffects()
        {
            // 포인트 라이트 펄스 애니메이션
            if (portalLight != null)
            {
                float t = (Mathf.Sin(Time.time * lightPulseSpeed) + 1f) * 0.5f;
                portalLight.intensity = Mathf.Lerp(minLightIntensity, maxLightIntensity, t);
            }

            // 바닥 및 공중 룬 링 회전
            if (rotatingRings != null)
            {
                for (int i = 0; i < rotatingRings.Length; i++)
                {
                    if (rotatingRings[i] == null)
                    {
                        continue;
                    }

                    float speed = (ringRotationSpeeds != null && i < ringRotationSpeeds.Length)
                        ? ringRotationSpeeds[i]
                        : 20f * (i % 2 == 0 ? 1f : -1f);

                    rotatingRings[i].Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
                }
            }
        }

        /// <summary>
        /// 텔레포트 시퀀스를 시작한다. 지연 시간 후 설정된 목적지로 이동한다.
        /// </summary>
        public void StartTeleportSequence(GameObject player)
        {
            if (isTeleporting || isCoolingDown)
            {
                return;
            }

            StartCoroutine(TeleportRoutine(player));
        }

        private IEnumerator TeleportRoutine(GameObject player)
        {
            isTeleporting = true;
            onTeleportStarted?.Invoke();

            // 진입 파티클 및 사운드 재생
            if (enterBurstEffect != null)
            {
                enterBurstEffect.Play();
            }

            if (audioSource != null && teleportSound != null)
            {
                audioSource.PlayOneShot(teleportSound);
            }

            if (teleportDelay > 0f)
            {
                yield return new WaitForSeconds(teleportDelay);
            }

            ExecuteTeleport(player);

            isTeleporting = false;
            StartCoroutine(CooldownRoutine());
        }

        /// <summary>
        /// 실제 씬 이동 또는 좌표 이동을 수행한다.
        /// </summary>
        private void ExecuteTeleport(GameObject player)
        {
            switch (teleportType)
            {
                case PortalTeleportType.SceneLoad:
                    if (string.IsNullOrEmpty(targetSceneTag))
                    {
                        DebugLogManager.GenerateErrorMessage<MapPortalController>("목표 씬 태그(targetSceneTag)가 설정되어 있지 않습니다.");
                        return;
                    }

                    if (SceneLoadManager.Instance == null)
                    {
                        DebugLogManager.GenerateErrorMessage<MapPortalController>("SceneLoadManager.Instance가 null입니다.");
                        return;
                    }

                    SceneLoadManager.Instance.LoadSceneByTags(targetSceneTag);
                    break;

                case PortalTeleportType.CoordinateTeleport:
                    if (player != null)
                    {
                        Vector3 destPos = targetDestination != null ? targetDestination.position : targetCoordinate;
                        Quaternion destRot = targetDestination != null ? targetDestination.rotation : player.transform.rotation;

                        // Rigidbody가 있으면 물리 위치 동기화
                        if (player.TryGetComponent(out Rigidbody rb))
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                            rb.position = destPos;
                            rb.rotation = destRot;
                        }
                        else
                        {
                            player.transform.position = destPos;
                            player.transform.rotation = destRot;
                        }

                        onTeleportCompleted?.Invoke();
                    }
                    break;
            }
        }

        private IEnumerator CooldownRoutine()
        {
            isCoolingDown = true;
            yield return new WaitForSeconds(cooldownTime);
            isCoolingDown = false;
        }

        /// <summary>
        /// 외부 스크립트에서 목적지 씬 태그를 동적으로 설정할 수 있는 편의 메서드.
        /// </summary>
        public void SetTargetSceneTag(string sceneTag)
        {
            targetSceneTag = sceneTag;
            teleportType = PortalTeleportType.SceneLoad;
        }

        /// <summary>
        /// 외부 스크립트에서 좌표 이동 목적지를 동적으로 설정할 수 있는 편의 메서드.
        /// </summary>
        public void SetTargetDestination(Transform destination)
        {
            targetDestination = destination;
            teleportType = PortalTeleportType.CoordinateTeleport;
        }
        #endregion
    }
}
