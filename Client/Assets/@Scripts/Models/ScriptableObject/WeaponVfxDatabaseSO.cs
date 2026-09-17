using System.Collections.Generic;
using Incheol.Models.Define;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Models.SO
{
    /// <summary>
    /// WeaponType별 공격(스윙)/피격(임팩트) 이펙트 매핑 테이블. ItemDatabaseSO/AddressableAssetModelSO와
    /// 같은 패턴 - 디자이너가 Inspector에서 무기 타입별 이펙트를 추가/수정하고, 런타임에는 WeaponType으로 조회만 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponVfxDatabaseSO", menuName = "ScriptableObjectAssets/WeaponVfxDatabase")]
    public class WeaponVfxDatabaseSO : ScriptableObject
    {
        public List<WeaponVfxEntry> entries = new List<WeaponVfxEntry>();

        private Dictionary<WeaponType, WeaponVfxEntry> entryDictionary;

        /// <summary>
        /// weaponType으로 이펙트 항목을 조회한다. 등록되지 않은 타입이면 false를 반환한다(호출측이 조용히 스킵).
        /// 최초 호출 시 딕셔너리를 한 번만 구성해 캐시한다.
        /// </summary>
        public bool TryGetEntry(WeaponType weaponType, out WeaponVfxEntry entry)
        {
            if (entryDictionary == null)
            {
                BuildDictionary();
            }

            return entryDictionary.TryGetValue(weaponType, out entry);
        }

        private void BuildDictionary()
        {
            entryDictionary = new Dictionary<WeaponType, WeaponVfxEntry>();

            if (entries == null)
            {
                return;
            }

            foreach (WeaponVfxEntry entry in entries)
            {
                if (entry == null || entry.weaponType == WeaponType.None)
                {
                    continue;
                }

                entryDictionary[entry.weaponType] = entry;
            }
        }

#if UNITY_EDITOR
        // 같은 weaponType을 실수로 중복 등록하면 TryGetEntry가 항상 먼저 것만 반환하게 되어 원인 파악이 어려우므로
        // 에디터 타임에 미리 잡아낸다(ItemDatabaseSO.OnValidate와 같은 패턴).
        private void OnValidate()
        {
            entryDictionary = null; // Inspector에서 수정할 때마다 캐시를 무효화해 항상 최신 상태를 반영한다.

            if (entries == null)
            {
                return;
            }

            var seenTypes = new HashSet<WeaponType>();
            foreach (WeaponVfxEntry entry in entries)
            {
                if (entry == null || entry.weaponType == WeaponType.None)
                {
                    continue;
                }

                if (!seenTypes.Add(entry.weaponType))
                {
                    DebugLogManager.GenerateErrorMessage<WeaponVfxDatabaseSO>($"중복된 weaponType이 있습니다 : {entry.weaponType}");
                }
            }
        }
#endif
    }
}
