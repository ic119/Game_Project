using System.Collections.Generic;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Models.SO
{
    /// <summary>
    /// 스킬 id별 이펙트 매핑 테이블. WeaponVfxDatabaseSO/ItemDatabaseSO와 같은 패턴 - 디자이너가 Inspector에서 스킬마다 이펙트를 고르고
    /// 크기/위치를 조정하고, 런타임에는 스킬 id로 조회만 한다. 이펙트를 바꾸려고 코드를 고칠 필요가 없다.
    /// </summary>
    [CreateAssetMenu(fileName = "SkillVfxDatabaseSO", menuName = "ScriptableObjectAssets/SkillVfxDatabase")]
    public class SkillVfxDatabaseSO : ScriptableObject
    {
        public List<SkillVfxEntry> entries = new List<SkillVfxEntry>();

        private Dictionary<string, SkillVfxEntry> entryDictionary;

        /// <summary>
        /// skillId로 이펙트 항목을 조회한다. 등록되지 않은 스킬이면 false(호출측이 조용히 스킵). 최초 호출 시 딕셔너리를 한 번만 구성해 캐시한다.
        /// </summary>
        public bool TryGetEntry(string skillId, out SkillVfxEntry entry)
        {
            if (entryDictionary == null)
            {
                BuildDictionary();
            }

            return entryDictionary.TryGetValue(skillId, out entry);
        }

        private void BuildDictionary()
        {
            entryDictionary = new Dictionary<string, SkillVfxEntry>();

            if (entries == null)
            {
                return;
            }

            foreach (SkillVfxEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.skillId))
                {
                    continue;
                }

                entryDictionary[entry.skillId] = entry;
            }
        }

#if UNITY_EDITOR
        // 중복 id나 SkillTable에 없는 id(오타)는 이펙트가 조용히 안 나오는 원인이 되므로 에디터 타임에 잡아낸다.
        private void OnValidate()
        {
            entryDictionary = null; // Inspector에서 수정할 때마다 캐시를 무효화한다.

            if (entries == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (SkillVfxEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.skillId))
                {
                    continue;
                }

                if (!seen.Add(entry.skillId))
                {
                    DebugLogManager.GenerateErrorMessage<SkillVfxDatabaseSO>($"중복된 skillId가 있습니다 : {entry.skillId}");
                }

                bool known = false;
                foreach (SkillTable.Entry skill in SkillTable.All)
                {
                    if (skill.Id == entry.skillId)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    DebugLogManager.GenerateErrorMessage<SkillVfxDatabaseSO>($"SkillTable에 없는 skillId입니다 : {entry.skillId}");
                }
            }
        }
#endif
    }
}
