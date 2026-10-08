using Incheol.Modules;
using UnityEngine;

namespace Incheol.Utils
{
    /// <summary>
    /// ObjectPoolManager로 대여/반환되는 파티클 이펙트 프리팹에 붙이는 공용 컴포넌트.
    /// 대여될 때마다 모든 자식 ParticleSystem을 처음부터 다시 재생하고, 재생이 끝날 만큼의 시간이
    /// 지나면 스스로 풀에 반환한다 - 호출측(WeaponVfxManager 등)이 타이머를 따로 관리할 필요가 없다.
    /// 대상 프리팹은 Loop=false인 파티클로만 구성되어 있어야 한다(Loop 이펙트는 스스로 끝나지 않으므로
    /// 별도의 정지 조건이 필요하다).
    /// </summary>
    public class PooledEffect : MonoBehaviour, IPoolable
    {
        private ParticleSystem[] particleSystems;
        private float lifetimeSeconds;
        private float releaseAtTime;
        private bool isWaitingForRelease;

        private void Awake()
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            lifetimeSeconds = ComputeLifetimeSeconds();
        }

        private void Update()
        {
            if (isWaitingForRelease && Time.time >= releaseAtTime)
            {
                isWaitingForRelease = false;
                ObjectPoolManager.Instance?.Release(gameObject);
            }
        }

        /// <summary>
        /// 풀에서 대여될 때마다 하나씩 늘어나는 번호. 이펙트를 대여한 쪽이 "내가 빌린 그 이펙트인지"를 나중에 확인하는 데 쓴다 -
        /// 스스로 풀에 반환된 인스턴스가 다른 곳에 다시 대여됐다면 번호가 달라진다(SkillVfxManager가 스킬 취소 때 남의 이펙트를 끄지 않게 한다).
        /// </summary>
        public int LeaseId { get; private set; }

        public void OnGetFromPool()
        {
            LeaseId++;

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                particleSystem.Clear(true);
                particleSystem.Play(true);
            }

            releaseAtTime = Time.time + lifetimeSeconds;
            isWaitingForRelease = true;
        }

        public void OnReleaseToPool()
        {
            isWaitingForRelease = false;

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        // 자식 ParticleSystem 중 가장 늦게 끝나는 것(재생시간 + 파티클 수명) 기준으로 전체 재생시간을 추정한다.
        private float ComputeLifetimeSeconds()
        {
            const float minimumLifetime = 0.1f;
            float maxLifetime = minimumLifetime;

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                ParticleSystem.MainModule main = particleSystem.main;
                float total = main.duration + main.startLifetime.constantMax;
                maxLifetime = Mathf.Max(maxLifetime, total);
            }

            return maxLifetime;
        }
    }
}
