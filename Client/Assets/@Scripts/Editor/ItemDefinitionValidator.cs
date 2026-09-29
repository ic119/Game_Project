using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Incheol.Models.SO;
using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    /// <summary>
    /// 아이템 정의는 Client(ItemDatabaseSO)와 Server(GameServer/Items/ItemDefinitions.json) 양쪽에
    /// 수동으로 중복 입력된다(ItemDefinition.cs 주석 참고 - "장비/물약 아이템을 추가할 때 ItemDatabaseSO와
    /// ItemDefinitions.json을 함께 갱신해야 한다"). 사람이 한쪽만 고치고 잊어버리면 조용히 어긋나므로,
    /// 커밋/빌드 전에 이 메뉴로 두 정의가 일치하는지 확인한다.
    /// </summary>
    public static class ItemDefinitionValidator
    {
        // Application.dataPath(Client/Assets)에서 Server/MainServer/GameServer/Items/ItemDefinitions.json까지의 상대 경로.
        private const string ServerItemDefinitionsRelativePath = "/../../Server/MainServer/GameServer/Items/ItemDefinitions.json";

        [MenuItem("Tools/아이템 정의 검증")]
        public static void Validate()
        {
            ItemDatabaseSO database = FindItemDatabase();
            if (database == null)
            {
                Debug.LogError("[ItemDefinitionValidator] ItemDatabaseSO 에셋을 찾지 못했습니다.");
                return;
            }

            string jsonPath = Path.GetFullPath(Application.dataPath + ServerItemDefinitionsRelativePath);
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[ItemDefinitionValidator] 서버 아이템 정의 파일을 찾지 못했습니다 : {jsonPath}");
                return;
            }

            Dictionary<string, ServerItemEntry> serverEntries = ParseServerDefinitions(File.ReadAllText(jsonPath));
            int mismatchCount = 0;
            var clientItemIds = new HashSet<string>();

            foreach (ItemData clientItem in database.items)
            {
                if (clientItem == null || string.IsNullOrEmpty(clientItem.itemId))
                {
                    continue;
                }

                clientItemIds.Add(clientItem.itemId);

                bool isEquipment = clientItem.itemType == ItemType.Eqiupment;
                bool isPotion = clientItem.itemType == ItemType.Potion;
                if (!isEquipment && !isPotion)
                {
                    continue; // 전투 스탯/회복에 영향 없는 아이템(General 등)은 서버 정의와 맞출 필요가 없다.
                }

                if (!serverEntries.TryGetValue(clientItem.itemId, out ServerItemEntry serverEntry))
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}'가 서버 ItemDefinitions.json에 없습니다.");
                    mismatchCount++;
                    continue;
                }

                string expectedEquipSlot = isEquipment ? clientItem.equipSlotType.ToString() : null;
                if (serverEntry.EquipSlot != expectedEquipSlot)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' equipSlot 불일치 : Client={expectedEquipSlot ?? "(none)"}, Server={serverEntry.EquipSlot ?? "(none)"}");
                    mismatchCount++;
                }

                if (serverEntry.BonusAttackPower != clientItem.bonusAttackPower)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' bonusAttackPower 불일치 : Client={clientItem.bonusAttackPower}, Server={serverEntry.BonusAttackPower}");
                    mismatchCount++;
                }

                if (serverEntry.BonusDefense != clientItem.bonusDefense)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' bonusDefense 불일치 : Client={clientItem.bonusDefense}, Server={serverEntry.BonusDefense}");
                    mismatchCount++;
                }

                if (serverEntry.HealPercent != clientItem.healPercent)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' healPercent 불일치 : Client={clientItem.healPercent}, Server={serverEntry.HealPercent}");
                    mismatchCount++;
                }
            }

            foreach (string serverItemId in serverEntries.Keys)
            {
                if (!clientItemIds.Contains(serverItemId))
                {
                    Debug.LogWarning($"[ItemDefinitionValidator] '{serverItemId}'가 서버에만 정의돼 있고 Client ItemDatabaseSO에는 없습니다.");
                }
            }

            if (mismatchCount == 0)
            {
                Debug.Log("[ItemDefinitionValidator] Client/Server 아이템 정의가 모두 일치합니다.");
            }
            else
            {
                Debug.LogError($"[ItemDefinitionValidator] 불일치 {mismatchCount}건 발견.");
            }
        }

        private static ItemDatabaseSO FindItemDatabase()
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDatabaseSO");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<ItemDatabaseSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ItemDefinitions.json은 "itemId": { 고정된 필드들 } 형태의 평평한 구조(중첩 객체/배열 없음)라,
        // 이 구조에 한정한 정규식 파싱으로 충분하다 - 별도 JSON 패키지를 새로 추가하지 않기 위함이다.
        // 필드가 중첩 구조로 바뀌면 이 파싱은 더 이상 맞지 않으니 제대로 된 JSON 파서로 바꿔야 한다.
        private static Dictionary<string, ServerItemEntry> ParseServerDefinitions(string json)
        {
            var entries = new Dictionary<string, ServerItemEntry>();

            foreach (Match blockMatch in Regex.Matches(json, "\"(?<id>[^\"]+)\"\\s*:\\s*\\{(?<body>[^{}]*)\\}"))
            {
                string body = blockMatch.Groups["body"].Value;
                entries[blockMatch.Groups["id"].Value] = new ServerItemEntry
                {
                    BonusAttackPower = ExtractInt(body, "BonusAttackPower"),
                    BonusDefense = ExtractInt(body, "BonusDefense"),
                    HealPercent = ExtractInt(body, "HealPercent"),
                    EquipSlot = ExtractString(body, "EquipSlot")
                };
            }

            return entries;
        }

        private static int ExtractInt(string body, string field)
        {
            Match match = Regex.Match(body, $"\"{field}\"\\s*:\\s*(-?\\d+)", RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static string ExtractString(string body, string field)
        {
            Match match = Regex.Match(body, $"\"{field}\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private class ServerItemEntry
        {
            public int BonusAttackPower;
            public int BonusDefense;
            public int HealPercent;
            public string EquipSlot;
        }
    }
}
