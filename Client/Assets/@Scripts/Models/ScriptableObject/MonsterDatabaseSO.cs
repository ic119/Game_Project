using System.Collections.Generic;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Models.SO
{
    /// <summary>
    /// 몬스터 종류별 외형(Addressable 키) 매핑 테이블. ItemDatabaseSO/WeaponVfxDatabaseSO와 같은 패턴 -
    /// 디자이너가 Inspector에서 몬스터 종류를 추가/수정하고, 런타임에는 monsterType(서버 문자열)으로 조회만 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterDatabaseSO", menuName = "ScriptableObjectAssets/MonsterDatabase")]
    public class MonsterDatabaseSO : ScriptableObject
    {
        public List<MonsterData> monsters = new List<MonsterData>();

        private Dictionary<string, MonsterData> monsterDictionary;

        /// <summary>
        /// monsterType으로 MonsterData를 조회한다. 최초 호출 시 딕셔너리를 한 번만 구성해 캐시한다.
        /// </summary>
        public bool TryGetByType(string _monsterType, out MonsterData data)
        {
            if (string.IsNullOrEmpty(_monsterType))
            {
                data = null;
                return false;
            }

            if (monsterDictionary == null)
            {
                BuildDictionary();
            }

            return monsterDictionary.TryGetValue(_monsterType, out data);
        }

        private void BuildDictionary()
        {
            monsterDictionary = new Dictionary<string, MonsterData>();

            if (monsters == null)
            {
                return;
            }

            foreach (MonsterData monster in monsters)
            {
                if (monster == null || string.IsNullOrEmpty(monster.monsterType))
                {
                    continue;
                }

                monsterDictionary[monster.monsterType] = monster;
            }
        }

#if UNITY_EDITOR
        // 같은 monsterType을 실수로 중복 등록하면 TryGetByType이 항상 먼저 것만 반환하게 되어 원인 파악이
        // 어려우므로 에디터 타임에 미리 잡아낸다(ItemDatabaseSO.OnValidate와 같은 패턴).
        private void OnValidate()
        {
            monsterDictionary = null; // Inspector에서 수정할 때마다 캐시를 무효화해 항상 최신 상태를 반영한다.

            if (monsters == null)
            {
                return;
            }

            var seenTypes = new HashSet<string>();
            foreach (MonsterData monster in monsters)
            {
                if (monster == null || string.IsNullOrEmpty(monster.monsterType))
                {
                    continue;
                }

                if (!seenTypes.Add(monster.monsterType))
                {
                    DebugLogManager.GenerateErrorMessage<MonsterDatabaseSO>($"중복된 monsterType이 있습니다 : {monster.monsterType}");
                }
            }
        }
#endif
    }
}
