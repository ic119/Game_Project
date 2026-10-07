using Incheol.Modules;
using UnityEngine;

namespace Incheol.Utils
{
    /// <summary>
    /// ObjectPoolManager로 대여/반환되는 "반복 재생(Loop)" 파티클 이펙트(날아가는 투사체 등)에 붙이는 컴포넌트.
    /// PooledEffect와 달리 스스로 풀에 반환하지 않는다 - 반복 이펙트는 끝나지 않으므로 대여한 쪽(ProjectileVfxManager)이 필요한
    /// 시점에 Release해야 한다. 대여될 때 모든 자식 ParticleSystem을 처음부터 다시 재생하고, 반환될 때 멈추고 지운다.
    /// </summary>
    public class PooledLoopEffect : MonoBehaviour, IPoolable
    {
        private ParticleSystem[] particleSystems;

        private void Awake()
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        public void OnGetFromPool()
        {
            if (particleSystems == null)
            {
                particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            }

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                particleSystem.Clear(true);
                particleSystem.Play(true);
            }
        }

        public void OnReleaseToPool()
        {
            if (particleSystems == null)
            {
                return;
            }

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
