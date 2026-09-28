using GameServer.Items;

namespace GameServer.Combat
{
    // Client Assets/@Scripts/Model/Combat/CombatStatComponent.cs(ApplyFromUserStats/SetEquipmentBonus)와
    // 동일한 공식을 서버 권위로 재현한다. Game_EnterRequest/Game_StatUpdateRequest로 클라이언트가 보고하는
    // AttackPower/Defense 값을 그대로 신뢰하지 않고, ClientSession이 이 계산 결과로 항상 덮어쓴다 - 입력값인
    // CharacterSnapshot(str/agi/레벨/장착 아이템)은 클라이언트가 아니라 MainServer(DB)에서 가져온 값이라
    // 위조할 수 없다. 공식이 바뀌면 CombatStatComponent/HealthComponent와 이 클래스를 함께 수정해야 한다.
    public static class CombatStatCalculator
    {
        // 레벨 1당 늘어나는 최대 체력. GameRoom이 세션 중 레벨업할 때도 이 값으로 최대 체력을 올린다.
        public const int MaxHpPerLevel = 10;

        // Client HealthComponent.ApplyFromUserStats와 동일한 임시 공식: 기본 100 + agi 1당 5 + 레벨 1당 10(1레벨 제외).
        public static int CalculateMaxHp(CharacterSnapshot snapshot)
        {
            return 100 + snapshot.Agi * 5 + Math.Max(0, snapshot.Level - 1) * MaxHpPerLevel;
        }

        // 방어력을 적용한 최종 피해량. 방어력이 아무리 높아도 최소 1은 들어간다(Client CombatCalculator.ApplyDefense와 동일).
        public static int ApplyDefense(int rawDamage, int defense)
        {
            return Math.Max(1, rawDamage - defense);
        }

        public static (int AttackPower, int Defense) Calculate(CharacterSnapshot snapshot)
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
