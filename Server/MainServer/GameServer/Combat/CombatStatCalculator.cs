using GameServer.Items;

namespace GameServer.Combat
{
    // Client Assets/@Scripts/Model/Combat/CombatStatComponent.cs(ApplyFromUserStats/SetEquipmentBonus)와
    // 동일한 공식을 서버 권위로 재현한다. Game_EnterRequest/Game_StatUpdateRequest로 클라이언트가 보고하는
    // AttackPower/Defense 값을 그대로 신뢰하지 않고, ClientSession이 이 계산 결과로 항상 덮어쓴다 - 입력값인
    // CharacterCombatSnapshot(str/agi/장착 아이템)은 클라이언트가 아니라 MainServer(DB)에서 가져온 값이라
    // 위조할 수 없다. 공식이 바뀌면 CombatStatComponent와 이 클래스를 함께 수정해야 한다.
    public static class CombatStatCalculator
    {
        public static (int AttackPower, int Defense) Calculate(CharacterCombatSnapshot snapshot)
        {
            // str 1당 공격력 1, agi 2당 방어력 1 - CombatStatComponent.ApplyFromUserStats와 동일한 임시 공식.
            int attackPower = snapshot.Str;
            int defense = snapshot.Agi / 2;

            foreach (string itemId in snapshot.EquippedItemIds)
            {
                if (ItemCatalog.TryGet(itemId, out ItemDefinition definition))
                {
                    attackPower += definition.BonusAttackPower;
                    defense += definition.BonusDefense;
                }
            }

            return (attackPower, defense);
        }
    }
}
