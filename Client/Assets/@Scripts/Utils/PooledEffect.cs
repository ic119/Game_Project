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

        public void OnGetFromPool()
        {
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
