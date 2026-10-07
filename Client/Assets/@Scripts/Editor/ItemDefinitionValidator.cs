using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Incheol.Models.SO;
using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    /// <summary>
    /// 아이템 정의는 Client(ItemDatabaseSO)와 Server(GameServer/Items/ItemDefinitions.json) 양쪽에
    /// 존재해야 한다(ItemDefinition.cs 주석 참고). 예전에는 둘 다 손으로 입력해서 한쪽만 고치고 잊어버리면
    /// 조용히 어긋났다 - 이제 ItemDatabaseSO를 원본으로 삼고 Generate()로 서버 JSON을 그 내용으로 새로
    /// 써서 중복 입력 자체를 없앤다. Validate()는 그래도 남겨둔다 - 누군가 서버 JSON을 손으로 고치거나
    /// Generate를 깜빡하고 커밋한 경우를 잡아내는 안전망이다.
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

                if (!serverEntries.TryGetValue(clientItem.itemId, out ServerItemEntry serverEntry))
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}'가 서버 ItemDefinitions.json에 없습니다.");
                    mismatchCount++;
                    continue;
                }

                // Grade는 모든 아이템 종류(General 포함)에 대해 등급 무작위 드롭 풀 구성 근거가 되므로 항상 비교한다.
                string expectedGrade = clientItem.itemGrade.ToString();
                if (serverEntry.Grade != expectedGrade)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' grade 불일치 : Client={expectedGrade}, Server={serverEntry.Grade ?? "(none)"}");
                    mismatchCount++;
                }

                bool isEquipment = clientItem.itemType == ItemType.Eqiupment;
                bool isPotion = clientItem.itemType == ItemType.Potion;
                if (!isEquipment && !isPotion)
                {
                    continue; // 전투 스탯/회복에 영향 없는 아이템(General 등)은 그 외 필드는 서버 정의와 맞출 필요가 없다.
                }

                string expectedEquipSlot = isEquipment ? clientItem.equipSlotType.ToString() : null;
                if (serverEntry.EquipSlot != expectedEquipSlot)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' equipSlot 불일치 : Client={expectedEquipSlot ?? "(none)"}, Server={serverEntry.EquipSlot ?? "(none)"}");
                    mismatchCount++;
                }

                // 무기가 아니면(또는 종류 미지정이면) 서버 JSON에 쓰지 않으므로 없음으로 본다(Generate와 같은 규칙).
                string expectedWeaponType = isEquipment && clientItem.equipSlotType == EquipmentSlotType.Weapon && clientItem.weaponType != WeaponType.None
                    ? clientItem.weaponType.ToString()
                    : null;
                if (serverEntry.WeaponType != expectedWeaponType)
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' weaponType 불일치 : Client={expectedWeaponType ?? "(none)"}, Server={serverEntry.WeaponType ?? "(none)"}");
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

                // 물약이 아니면 서버 JSON에 쓰지 않으므로 0으로 본다(Generate와 같은 규칙).
                float expectedCooldown = isPotion ? clientItem.useCooldownSeconds : 0f;
                if (!Mathf.Approximately(serverEntry.UseCooldownSeconds, expectedCooldown))
                {
                    Debug.LogError($"[ItemDefinitionValidator] '{clientItem.itemId}' useCooldownSeconds 불일치 : Client={expectedCooldown}, Server={serverEntry.UseCooldownSeconds}");
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

        /// <summary>
        /// ItemDatabaseSO의 내용으로 서버 ItemDefinitions.json을 새로 쓴다. 기존 파일 내용은 무시하고
        /// 완전히 덮어쓰므로, 서버 JSON을 손으로 고친 게 있었다면 이 실행으로 사라진다(그게 목적이다 -
        /// ItemDatabaseSO만 원본으로 남긴다). 실행 뒤 바로 Validate()로 결과를 재확인한다.
        /// </summary>
        [MenuItem("Tools/아이템 정의 서버 JSON 생성")]
        public static void Generate()
        {
            ItemDatabaseSO database = FindItemDatabase();
            if (database == null)
            {
                Debug.LogError("[ItemDefinitionValidator] ItemDatabaseSO 에셋을 찾지 못했습니다.");
                return;
            }

            string jsonPath = Path.GetFullPath(Application.dataPath + ServerItemDefinitionsRelativePath);
            string json = BuildServerDefinitionsJson(database);
            File.WriteAllText(jsonPath, json);

            int itemCount = 0;
            foreach (ItemData item in database.items)
            {
                if (item != null && !string.IsNullOrEmpty(item.itemId))
                {
                    itemCount++;
                }
            }

            Debug.Log($"[ItemDefinitionValidator] 서버 아이템 정의 생성 완료 : {jsonPath} ({itemCount}종)");
            Validate();
        }

        private static string BuildServerDefinitionsJson(ItemDatabaseSO database)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");

            bool isFirstEntry = true;
            foreach (ItemData item in database.items)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId))
                {
                    continue;
                }

                if (!isFirstEntry)
                {
                    builder.Append(",\n");
                }

                isFirstEntry = false;
                AppendItemEntry(builder, item);
            }

            builder.Append("\n}\n");
            return builder.ToString();
        }

        // ItemDefinition.cs의 기본값과 같은 필드는 생략한다 - 기존 ItemDefinitions.json이 손으로 작성되던
        // 시절부터 그렇게 써왔고(예: 물약이 아니면 healPercent를 안 씀), 그 스타일을 그대로 따른다.
        private static void AppendItemEntry(StringBuilder builder, ItemData item)
        {
            builder.Append($"  \"{item.itemId}\": {{ \"name\": \"{EscapeJsonString(item.itemName)}\", \"maxStack\": {item.maxStackCount}, \"grade\": \"{item.itemGrade}\"");

            if (item.itemType == ItemType.Potion && item.healPercent > 0)
            {
                builder.Append($", \"healPercent\": {item.healPercent}");
            }

            // 물약 재사용 대기시간(초). 소수점 표기가 로케일에 흔들리지 않게 InvariantCulture로 쓴다.
            if (item.itemType == ItemType.Potion && item.useCooldownSeconds > 0f)
            {
                builder.Append($", \"useCooldownSeconds\": {item.useCooldownSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            if (item.itemType == ItemType.Eqiupment && item.equipSlotType != EquipmentSlotType.None)
            {
                if (item.bonusAttackPower != 0)
                {
                    builder.Append($", \"bonusAttackPower\": {item.bonusAttackPower}");
                }

                if (item.bonusDefense != 0)
                {
                    builder.Append($", \"bonusDefense\": {item.bonusDefense}");
                }

                builder.Append($", \"equipSlot\": \"{item.equipSlotType}\"");

                // 무기 종류. 서버가 액티브 스킬(GameRoom.TryBeginSkillCast)에서 "지금 들고 있는 무기의 스킬"을 정하는 근거다.
                if (item.equipSlotType == EquipmentSlotType.Weapon && item.weaponType != WeaponType.None)
                {
                    builder.Append($", \"weaponType\": \"{item.weaponType}\"");
                }
            }

            builder.Append(" }");
        }

        private static string EscapeJsonString(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
                    UseCooldownSeconds = ExtractFloat(body, "UseCooldownSeconds"),
                    EquipSlot = ExtractString(body, "EquipSlot"),
                    WeaponType = ExtractString(body, "WeaponType"),
                    Grade = ExtractString(body, "Grade")
                };
            }

            return entries;
        }

        private static int ExtractInt(string body, string field)
        {
            Match match = Regex.Match(body, $"\"{field}\"\\s*:\\s*(-?\\d+)", RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static float ExtractFloat(string body, string field)
        {
            Match match = Regex.Match(body, $"\"{field}\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
            return match.Success ? float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0f;
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
            public float UseCooldownSeconds;
            public string EquipSlot;
            public string WeaponType;
            public string Grade;
        }
    }
}
