using System.Collections.Generic;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Models.SO
{
    /// <summary>
    /// 게임에 존재하는 모든 아이템의 정적 정의를 담는 테이블. AddressableAssetModelSO/SceneDataModelSO와
    /// 같은 패턴 - 디자이너가 Inspector에서 아이템을 추가/수정하고, 런타임에는 itemId로 조회만 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabaseSO", menuName = "ScriptableObjectAssets/ItemDatabase")]
    public class ItemDatabaseSO : ScriptableObject
    {
        public List<ItemData> items = new List<ItemData>();

        private Dictionary<string, ItemData> itemDictionary;

        /// <summary>
        /// itemId로 ItemData를 조회한다. 최초 호출 시 딕셔너리를 한 번만 구성해 캐시한다.
        /// </summary>
        public ItemData FindById(string _itemId)
        {
            if (string.IsNullOrEmpty(_itemId))
            {
                return null;
            }

            if (itemDictionary == null)
            {
                BuildDictionary();
            }

            return itemDictionary.TryGetValue(_itemId, out ItemData data) ? data : null;
        }

        private void BuildDictionary()
        {
            itemDictionary = new Dictionary<string, ItemData>();

            if (items == null)
            {
                return;
            }

            foreach (ItemData item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId))
                {
                    continue;
                }

                itemDictionary[item.itemId] = item;
            }
        }

#if UNITY_EDITOR
        // 같은 itemId를 실수로 중복 등록하면 FindById가 항상 먼저 것만 반환하게 되어 원인 파악이 어려우므로
        // 에디터 타임에 미리 잡아낸다(AddressableAssetModelSO.OnValidate와 같은 패턴).
        private void OnValidate()
        {
            itemDictionary = null; // Inspector에서 수정할 때마다 캐시를 무효화해 항상 최신 상태를 반영한다.

            if (items == null)
            {
                return;
            }

            var seenIds = new HashSet<string>();
            foreach (ItemData item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId))
                {
                    continue;
                }

                if (!seenIds.Add(item.itemId))
                {
                    DebugLogManager.GenerateErrorMessage<ItemDatabaseSO>($"중복된 itemId가 있습니다 : {item.itemId}");
                }
            }
        }
#endif
    }
}
