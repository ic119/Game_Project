using System;
using Incheol.Modules.Networking;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 서버(GameRoom)가 권위를 갖는 몬스터 개체 하나를 표현한다. RemoteCharacterController(원격 플레이어)와
    /// 동일한 패턴으로, 스냅샷 위치를 서버 시각과 함께 쌓아 두고(SnapshotInterpolationBuffer) 조금 과거 시점을 보간해 그린다 -
    /// 실제 인식/추적 판단은 서버(GameRoom.TickMonsterAi)가 하고 여기서는 결과만 반영한다.
    /// </summary>
    public class RemoteMonsterController : MonoBehaviour
    {
        [Tooltip("보간된 이동 속도(m/s)가 이 값을 넘으면 이동 애니메이션(IsMove)을 재생한다.")]
        [SerializeField, Min(0f)] private float moveSpeedThreshold = 0.2f;

        [Tooltip("멈춘 뒤 이 시간(초)이 지나야 정지 애니메이션(IsIdle)으로 바꾼다 - 스냅샷 사이 짧은 정지로 애니메이션이 깜빡이지 않게 한다.")]
        [SerializeField, Min(0f)] private float idleDelay = 0.15f;

        [Tooltip("지면을 찾을 때 서버 좌표보다 이만큼 위에서 아래로 레이를 쏜다 - 서버 높이보다 높은 언덕/계단 위에서도 지면을 찾기 위함.")]
        [SerializeField, Min(0f)] private float groundProbeHeight = 2f;

        [Tooltip("지면을 찾는 레이의 최대 길이(시작점부터). 이 안에 지면이 없으면 서버가 보낸 높이를 그대로 쓴다.")]
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 10f;

        // 지면 탐색용 레이캐스트 결과 버퍼(매 프레임 할당하지 않도록 재사용).
        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        private static readonly int GetHitHash = Animator.StringToHash("Get Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");
        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");

        // MushroomStance 기준 공격 모션 개수(Attack001~Attack003). AttackIndex는 1부터 시작한다.
        private const int AttackVariationCount = 3;

        private readonly SnapshotInterpolationBuffer interpolation = new();
        private Animator animator;
        private float lastMovingTime = float.NegativeInfinity;

        /// <summary>
        /// 이 인스턴스가 나타내는 서버측 몬스터 id. RemoteMonsterManager가 스폰 직후 Initialize로 채운다.
        /// 공격 대상 판정(PlayerAttackController)에서 콜라이더로부터 대상의 id를 즉시 얻는 데 쓴다.
        /// </summary>
        public long MonsterId { get; private set; }
        public string MonsterType { get; private set; }

        // 이름/등급/경험치는 MonsterDatabaseSO(정적 데이터)에서, MaxHp/CurrentHp는 서버(GameMonsterInfo)에서
        // 스폰 시점에 각각 한 번만 조회한 값을 여기 저장해둔다 - 몬스터 정보 UI가 매번 데이터베이스나
        // 네트워크 패킷을 다시 뒤지지 않고 이 인스턴스만 보면 되게 하기 위함이다.
        public string DisplayName { get; private set; }
        public ItemGrade Grade { get; private set; }
        public int ExpReward { get; private set; }
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }

        /// <summary>
        /// 서버 사망 알림을 받아 사망 연출 중이면 true(PlayDeath). 오브젝트는 연출이 끝날 때까지 남아 있으므로
        /// 공격 대상 판정은 이 값으로 걸러야 한다.
        /// </summary>
        public bool IsDead { get; private set; }

        /// <summary>
        /// CurrentHp가 바뀔 때(피격) 발생한다. 몬스터 정보 UI가 매 프레임 폴링하지 않고 이 이벤트만
        /// 구독하면 되도록 하기 위함이다. 인자는 (CurrentHp, MaxHp).
        /// </summary>
        public event Action<int, int> OnHpChanged;

        private void Awake()
        {
            interpolation.Reset(transform.position, transform.eulerAngles.y);
            animator = GetComponent<Animator>();

            // 위치는 서버가 정하고 이 컴포넌트가 매 프레임 transform에 직접 쓴다. 프리팹의 Rigidbody가 물리(중력/충돌)로 움직이는
            // 상태면 매 프레임 되돌려지는 위치와 싸우기만 하고(중력으로 떨어져도 다음 프레임에 서버 높이로 복귀), 플레이어와 부딪히면
            // 밀려났다 순간이동하듯 튄다. 콜라이더(공격 판정용)는 그대로 두고 물리 시뮬레이션에서만 뺀다.
            if (TryGetComponent(out Rigidbody body))
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        private void Update()
        {
            interpolation.Sample(ServerClock.NowMs - SnapshotInterpolationBuffer.InterpolationDelayMs, out Vector3 position, out float rotationY);
            position = SnapToGround(position);

            float speed = Time.deltaTime > 0f ? Vector3.Distance(transform.position, position) / Time.deltaTime : 0f;
            if (speed > moveSpeedThreshold)
            {
                lastMovingTime = Time.time;
            }

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));

            if (animator != null)
            {
                bool isMoving = Time.time - lastMovingTime < idleDelay;
                animator.SetBool(IsMoveHash, isMoving);
                animator.SetBool(IsIdleHash, !isMoving);
            }
        }

        /// <summary>
        /// 스폰 직후 RemoteMonsterManager가 한 번 호출한다. MonsterDatabaseSO에서 조회한 식별/표시 정보와
        /// 서버가 보낸 GameMonsterInfo의 스탯을 한 번에 채운다 - 이후 UI 등 다른 소비자는 이 인스턴스의
        /// 프로퍼티만 읽으면 되고 MonsterDatabaseSO나 원본 패킷을 다시 조회할 필요가 없다.
        /// </summary>
        public void Initialize(long monsterId, string monsterType, string displayName, ItemGrade grade, int expReward, int maxHp, int currentHp)
        {
            MonsterId = monsterId;
            MonsterType = monsterType;
            DisplayName = displayName;
            Grade = grade;
            ExpReward = expReward;
            MaxHp = maxHp;
            CurrentHp = currentHp;
        }

        /// <summary>
        /// Game_MonsterDamageBroadcast의 RemainingHp를 그대로 반영한다(서버 권위값이라 로컬 재계산 없음).
        /// CurrentHp 갱신 후 OnHpChanged를 발생시켜 구독 중인 UI를 즉시 갱신한다.
        /// </summary>
        public void ApplyDamage(int remainingHp)
        {
            CurrentHp = remainingHp;
            OnHpChanged?.Invoke(CurrentHp, MaxHp);
        }

        /// <summary>
        /// Game_WorldSnapshot(서버 방 틱의 추적/복귀 이동)으로 받은 위치를 서버 시각과 함께 보간 버퍼에 넣는다.
        /// </summary>
        public void AddSnapshot(double serverTimeMs, Vector3 position, float rotationY)
        {
            interpolation.Add(serverTimeMs, position, rotationY);
        }

        /// <summary>
        /// 스폰(시야 진입) 직후 서버가 알려준 위치/회전으로 보간 없이 즉시 배치한다.
        /// </summary>
        public void Warp(Vector3 position, float rotationY)
        {
            interpolation.Reset(position, rotationY);
            transform.SetPositionAndRotation(SnapToGround(position), Quaternion.Euler(0f, rotationY, 0f));
        }

        /// <summary>
        /// 서버 좌표의 높이(Y)를 발밑 지면 높이로 바꾼다. 서버(GameRoom)는 지형 정보가 없어 몬스터를 X/Z로만 움직이고, Y는 스폰 포인트
        /// 마커 높이(맵 프리팹의 마커 위치 그대로 - 예: Floor001_MushroomForest는 지면보다 0.61m 위)에 고정돼 있다. 그 값을 그대로 쓰면
        /// 몬스터가 떠 있거나, 경사를 따라 쫓아올 때 땅에 파묻힌다. 높이는 보이는 것에만 영향이 있으므로 클라이언트가 지면에 맞춘다.
        /// 지면은 Rigidbody가 없는 고정 콜라이더로 본다 - 같은 레이에 걸린 플레이어/다른 몬스터(Rigidbody 보유)와 자기 자신은 건너뛴다.
        /// </summary>
        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * groundProbeHeight;
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, groundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.rigidbody != null || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    position.y = hit.point.y;
                }
            }

            return position;
        }

        /// <summary>
        /// 이 몬스터가 대상(플레이어)을 공격하는 순간 재생한다. MushroomStance의 Attack001~Attack003 중
        /// 하나를 매번 무작위로 골라 AttackIndex에 채운 뒤 Attack 트리거를 발동한다.
        /// </summary>
        public void PlayAttack()
        {
            if (animator == null)
            {
                return;
            }

            animator.SetInteger(AttackIndexHash, UnityEngine.Random.Range(1, AttackVariationCount + 1));
            animator.SetTrigger(AttackHash);
        }

        /// <summary>
        /// Game_MonsterDamageBroadcast 수신 시(사망 여부와 무관하게) 재생한다.
        /// </summary>
        public void PlayHitReaction()
        {
            if (animator != null)
            {
                animator.SetTrigger(GetHitHash);
            }
        }

        /// <summary>
        /// Game_MonsterDieBroadcast 수신 시 재생한다. 실제 오브젝트 제거는 RemoteMonsterManager가
        /// 애니메이션이 보일 시간을 준 뒤(DieAnimationDuration) 처리한다.
        /// </summary>
        public void PlayDeath()
        {
            IsDead = true;

            // 사망 연출 동안(RemoteMonsterManager.dieAnimationDuration) 오브젝트는 남지만 이미 죽은 몬스터다. 콜라이더를 그대로 두면
            // 공격 판정(PlayerAttackController.TryFindNearestTarget)이 이 시체를 가장 가까운 대상으로 골라, 바로 뒤의 살아 있는
            // 몬스터 대신 죽은 몬스터에 공격 요청을 보내고(서버는 무시) 타겟 HUD도 켜지지 않았다. 판정에서 빠지도록 끈다.
            foreach (Collider bodyCollider in GetComponentsInChildren<Collider>())
            {
                bodyCollider.enabled = false;
            }

            if (animator != null)
            {
                animator.SetTrigger(DieHash);
            }
        }
    }
}
