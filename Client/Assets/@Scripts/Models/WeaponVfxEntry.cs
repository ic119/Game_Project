using System;
using Incheol.Models.Define;

namespace Incheol.Models.SO
{
    // WeaponVfxDatabaseSO에 리스트로 등록되어 WeaponType으로 조회된다.
    // swingEffectKey/impactEffectKey가 AddressableAssetKey.None이면 해당 이펙트를 재생하지 않는다
    // (아직 이펙트가 준비되지 않은 무기 타입을 조용히 건너뛰기 위함).
    [Serializable]
    public class WeaponVfxEntry
    {
        public WeaponType weaponType = WeaponType.None;
        public AddressableAssetKey swingEffectKey = AddressableAssetKey.None;
        public AddressableAssetKey impactEffectKey = AddressableAssetKey.None;
    }
}
