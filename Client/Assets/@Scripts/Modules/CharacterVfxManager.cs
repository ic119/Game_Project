using Incheol.Models.Define;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// 레벨업/버프 등 캐릭터에 종속된(무기 타입과 무관한) 상태 연출 이펙트의 재생 진입점.
    /// WeaponVfxManager와 동일하게 실제 스폰/재사용은 ObjectPoolManager + PooledEffect가 담당하며,
    /// 이 매니저는 어떤 AddressableAssetKey를 캐릭터의 어느 앵커(EffectBone 등)에 재생할지만 결정한다.
    /// 캐릭터 프리팹에 이펙트를 미리 비활성 상태로 심어두는 대신, 필요한 순간에 풀에서 대여해 앵커 위치로
    /// 옮기고 재생이 끝나면 다시 반환하므로, 캐릭터 인스턴스 수/이펙트 종류가 늘어나도 상시 메모리 사용량이
    /// 늘어나지 않는다.
    /// </summary>
    public class CharacterVfxManager : SingletonObject<CharacterVfxManager>
    {
        protected override bool PersistAcrossScenes => true;

        /// <summary>
        /// 레벨업 순간 재생하는 이펙트. _anchor(보통 캐릭터의 EffectBone)의 월드 위치/회전에 그대로 스폰한다.
        /// </summary>
        public void PlayLevelUpEffect(Transform _anchor)
        {
            PlayEffect(AddressableAssetKey.LevelUp01, _anchor);
        }


        /// <summary>
        /// 회복포션 사용 순간 재생하는 이펙트. _anchor(보통 캐릭터의 EffectBone)의 월드 위치/회전에 그대로 스폰한다.
        /// </summary>
        public void PlayHpPotionEffect(Transform _anchor)
        {
            PlayEffect(AddressableAssetKey.HpPotion01, _anchor);
        }


        /// <summary>
        /// 몬스터 공격을 대쉬로 피한 순간 재생하는 이펙트. _anchor(보통 캐릭터의 EffectBone)의 월드 위치/회전에 그대로 스폰한다.
        /// </summary>
        public void PlayDodgeEffect(Transform _anchor)
        {
            PlayEffect(AddressableAssetKey.Dodge01, _anchor);
        }


        /// <summary>
        /// _key에 해당하는 이펙트를 _anchor 위치/회전에 스폰한다. 이후 버프 이펙트 등을 추가할 때도
        /// 이 메서드를 그대로 재사용하고, 공개 Play*Effect 메서드만 하나씩 늘리면 된다.
        /// </summary>
        private void PlayEffect(AddressableAssetKey _key, Transform _anchor)
        {
            if (_anchor == null || _key == AddressableAssetKey.None || ObjectPoolManager.Instance == null)
            {
                return;
            }

            ObjectPoolManager.Instance.Get(_key.ToString(), _anchor.position, _anchor.rotation);
        }
    }
}
