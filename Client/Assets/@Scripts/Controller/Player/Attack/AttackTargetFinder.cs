using Incheol.Models.Define;
using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 공격 대상 하나. 종류(몬스터/플레이어)에 따라 서버에 보낼 OpCode가 다르므로 id와 함께 종류도 담는다.
    /// Monster는 kind가 Monster일 때만 채워진다(UI 대상 표시용).
    /// </summary>
    public readonly struct AttackTarget
    {
        public readonly AttackTargetKind Kind;
        public readonly long Id;
        public readonly Transform Transform;
        public readonly RemoteMonsterController Monster;

        public AttackTarget(AttackTargetKind kind, long id, Transform transform, RemoteMonsterController monster)
        {
            Kind = kind;
            Id = id;
            Transform = transform;
            Monster = monster;
        }

        public bool IsNone => Kind == AttackTargetKind.None;
    }

    /// <summary>
    /// PlayerAttackController가 쓰는 대상 탐색. 근접 판정 구체 안의 가장 가까운 대상, 완드의 정면 부채꼴 대상, 공격 방향 보조 대상을
    /// 찾는다. 상태는 방향 보조가 고른 대상(콤보 도중 유지용)뿐이며, 입력/콤보/네트워크 전송은 알지 못한다.
    /// 판정 버퍼는 매 공격마다 할당하지 않도록 정적으로 재사용한다(방향 보조 버퍼는 바로 뒤이은 공격 판정이 덮어쓰지 않게 따로 둔다).
    /// </summary>
    public class AttackTargetFinder
    {
        // 방향 보조가 직전 대상을 놓지 않는 거리 배율(거리 제곱 기준 1.25^2). 조금 멀어져도 한동안은 유지한다.
        private const float AimAssistKeepRangeSqrMultiplier = 1.5625f;

        private static readonly Collider[] overlapBuffer = new Collider[16];
        private static readonly Collider[] aimAssistBuffer = new Collider[16];

        private readonly Transform owner;
        private RemoteMonsterController aimAssistTarget;

        public AttackTargetFinder(Transform owner)
        {
            this.owner = owner;
        }

        /// <summary>
        /// 근접 판정: origin 중심 radius 구체 안의 원격 플레이어/몬스터 중 owner에 가장 가까운 대상 하나.
        /// 사망 연출 중인 몬스터는 뺀다(콜라이더는 PlayDeath에서 꺼지지만, 같은 프레임에 걸린 경우까지 막는다).
        /// </summary>
        public AttackTarget FindNearestInSphere(Vector3 origin, float radius)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(origin, radius, overlapBuffer);
            AttackTarget best = default;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                if (!TryGetCandidate(hit, out AttackTarget candidate))
                {
                    continue;
                }

                float distanceSqr = (candidate.Transform.position - owner.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 완드가 노릴 대상: range 안의 살아 있는 몬스터/원격 플레이어 중, 정면에서 halfAngle 이내인 가장 가까운 하나. 없으면 None.
        /// </summary>
        public AttackTarget FindInCone(float range, float halfAngle)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(owner.position, range, overlapBuffer);
            Vector3 forward = owner.forward;
            forward.y = 0f;
            AttackTarget best = default;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                if (!TryGetCandidate(hit, out AttackTarget candidate))
                {
                    continue;
                }

                Vector3 toTarget = candidate.Transform.position - owner.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toTarget) > halfAngle)
                {
                    continue;
                }

                float distanceSqr = toTarget.sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 공격 방향 보조 대상(몬스터 전용 - 다른 플레이어 쪽으로 의도치 않게 돌지 않게 한다). 이어지는 타수는 직전 대상을
        /// (아직 가깝고 살아 있으면) 그대로 유지해 두 타 사이에 대상이 흔들리지 않게 한다.
        /// </summary>
        public RemoteMonsterController ResolveAimAssistTarget(float range)
        {
            if (aimAssistTarget != null && !aimAssistTarget.IsDead
                && (aimAssistTarget.transform.position - owner.position).sqrMagnitude <= range * range * AimAssistKeepRangeSqrMultiplier)
            {
                return aimAssistTarget;
            }

            aimAssistTarget = null;

            int count = Physics.OverlapSphereNonAlloc(owner.position, range, aimAssistBuffer);
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = aimAssistBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                RemoteMonsterController monster = hit.GetComponentInParent<RemoteMonsterController>();
                if (monster == null || monster.IsDead)
                {
                    continue;
                }

                float distanceSqr = (monster.transform.position - owner.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    aimAssistTarget = monster;
                }
            }

            return aimAssistTarget;
        }

        /// <summary>
        /// 콤보가 끝나거나 끊기면 방향 보조 대상을 비운다.
        /// </summary>
        public void ClearAimAssistTarget()
        {
            aimAssistTarget = null;
        }

        // 콜라이더에서 공격 대상 후보를 만든다. 원격 플레이어가 우선이고, 몬스터는 사망 연출 중이면 후보가 아니다.
        private static bool TryGetCandidate(Collider hit, out AttackTarget candidate)
        {
            RemoteCharacterController remotePlayer = hit.GetComponentInParent<RemoteCharacterController>();
            if (remotePlayer != null)
            {
                candidate = new AttackTarget(AttackTargetKind.Player, remotePlayer.PlayerId, remotePlayer.transform, null);
                return true;
            }

            RemoteMonsterController monster = hit.GetComponentInParent<RemoteMonsterController>();
            if (monster != null && !monster.IsDead)
            {
                candidate = new AttackTarget(AttackTargetKind.Monster, monster.MonsterId, monster.transform, monster);
                return true;
            }

            candidate = default;
            return false;
        }
    }
}
