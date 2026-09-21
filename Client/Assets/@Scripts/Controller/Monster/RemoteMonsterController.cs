using System;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 서버(GameRoom)가 권위를 갖는 몬스터 개체 하나를 표현한다. RemoteCharacterController(원격 플레이어)와
    /// 동일한 패턴으로, GameServerConnectManager가 수신한 목표 위치/회전을 향해 매 프레임 보간(Lerp/Slerp)만
    /// 한다 - 실제 인식/추적 판단은 서버(GameRoom.TickMonsterAiAsync)가 하고 여기서는 결과만 반영한다.
    /// </summary>
    public class RemoteMonsterController : MonoBehaviour
    {
        [Header("보간 속도")]
        [SerializeField, Min(0f)] private float positionLerpSpeed = 10f;
        [SerializeField, Min(0f)] private float rotationLerpSpeed = 10f;

        [Tooltip("목표 위치까지의 거리가 이 값 미만이면 정지 상태(IsIdle)로 간주한다.")]
        [SerializeField, Min(0f)] private float moveThreshold = 0.05f;

        private static readonly int GetHitHash = Animator.StringToHash("Get Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");
        private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
        private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");

        // MushroomStance 기준 공격 모션 개수(Attack001~Attack003). AttackIndex는 1부터 시작한다.
        private const int AttackVariationCount = 3;

        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Animator animator;

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
        /// CurrentHp가 바뀔 때(피격) 발생한다. 몬스터 정보 UI가 매 프레임 폴링하지 않고 이 이벤트만
        /// 구독하면 되도록 하기 위함이다. 인자는 (CurrentHp, MaxHp).
        /// </summary>
        public event Action<int, int> OnHpChanged;

        private void Awake()
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
            animator = GetComponent<Animator>();
        }

        private void Update()
        {
            bool isMoving = Vector3.Distance(transform.position, targetPosition) > moveThreshold;

            transform.position = Vector3.Lerp(transform.position, targetPosition, positionLerpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationLerpSpeed * Time.deltaTime);

            if (animator != null)
            {
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
        /// Game_MonsterMoveBroadcast(서버 AI 틱의 추적/복귀 이동)로 전달받은 목표 위치/회전을 갱신한다.
        /// </summary>
        public void SetTarget(Vector3 position, float rotationY)
        {
            targetPosition = position;
            targetRotation = Quaternion.Euler(0f, rotationY, 0f);
        }

        /// <summary>
        /// 스폰 직후 서버가 알려준 위치/회전으로 보간 없이 즉시 배치한다.
        /// </summary>
        public void Warp(Vector3 position, float rotationY)
        {
            targetPosition = position;
            targetRotation = Quaternion.Euler(0f, rotationY, 0f);
            transform.SetPositionAndRotation(position, targetRotation);
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
            if (animator != null)
            {
                animator.SetTrigger(DieHash);
            }
        }
    }
}
