using GameServer.Items;
using Shared;

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
        // agi에는 레벨 성장 보너스(StatGrowth)가 더해진다. level을 주면 그 값으로 계산한다 - 접속 중 레벨업하면 서버 메모리의 레벨이
        // DB(snapshot.Level)보다 앞서므로, 이미 오른 레벨 기준으로 계산해야 할 때 쓴다. 생략하면 snapshot.Level이다.
        public static int CalculateMaxHp(CharacterSnapshot snapshot, int? level = null)
        {
            return CalculateMaxHp(snapshot.Agi, level ?? snapshot.Level);
        }

        // 기본 agi(레벨 보너스 제외)와 레벨로 최대 체력을 계산한다. 세션 중 레벨업 때 서버가 들고 있는 기준값(PlayerInfo.BaseAgi)으로 쓴다.
        public static int CalculateMaxHp(int baseAgi, int level)
        {
            int agi = baseAgi + StatGrowth.BonusAtLevel(level);
            return 100 + agi * 5 + Math.Max(0, level - 1) * MaxHpPerLevel;
        }

        // 최대 마나 공식(체력과 같은 구조): 기본 30 + 지능 1당 3 + 레벨 1당 3(1레벨 제외). 지능에는 레벨 성장 보너스(StatGrowth)가 더해진다.
        // 값의 근거와 레벨별 표는 Server/마나_밸런싱_공식.txt에 있다. 클라이언트(Utils/ManaFormula.cs)와 반드시 같아야 하며
        // GameServer.Tests의 ClientFormulaParityTests가 같은 값을 내는지 검사한다.
        public const int BaseMaxMp = 30;
        public const int MaxMpPerIntel = 3;
        public const int MaxMpPerLevel = 3;

        // DB 스냅샷의 기본 지능과 레벨로 최대 마나를 계산한다. level을 주면 그 값으로 계산한다(CalculateMaxHp와 같은 이유).
        public static int CalculateMaxMp(CharacterSnapshot snapshot, int? level = null)
        {
            return CalculateMaxMp(snapshot.Intel, level ?? snapshot.Level);
        }

        // 기본 지능(레벨 보너스 제외)과 레벨로 최대 마나를 계산한다. 세션 중 레벨업 때 서버가 들고 있는 기준값(PlayerInfo.BaseIntel)으로 쓴다.
        public static int CalculateMaxMp(int baseIntel, int level)
        {
            int intel = baseIntel + StatGrowth.BonusAtLevel(level);
            return BaseMaxMp + intel * MaxMpPerIntel + Math.Max(0, level - 1) * MaxMpPerLevel;
        }

        // 초당 마나 회복량: 최대 마나의 percentPerSecond%. 최대 마나에 비례하므로 지능/레벨이 높을수록 절대 회복량도 늘어난다.
        public static float CalculateManaRegenPerSecond(int maxMp, double percentPerSecond)
        {
            return (float)(maxMp * percentPerSecond / 100.0);
        }

        // 물약 회복량: 최대 체력의 healPercent%, 최소 1(Client 쪽 기존 CombatCalculator.CalculateHealAmount와 같은 공식).
        public static int CalculateHealAmount(int maxHp, int healPercent)
        {
            return Math.Max(1, (int)Math.Round(maxHp * healPercent / 100f));
        }

        // 방어력을 적용한 최종 피해량. 방어력이 아무리 높아도 최소 1은 들어간다(Client CombatCalculator.ApplyDefense와 동일).
        public static int ApplyDefense(int rawDamage, int defense)
        {
            return Math.Max(1, rawDamage - defense);
        }

        // level 인자의 의미는 CalculateMaxHp와 같다(생략하면 snapshot.Level).
        public static (int AttackPower, int Defense) Calculate(CharacterSnapshot snapshot, int? level = null)
        {
            (int equipmentAttack, int equipmentDefense) = CalculateEquipmentBonus(snapshot);
            return Calculate(snapshot.Str, snapshot.Agi, level ?? snapshot.Level, equipmentAttack, equipmentDefense);
        }

        // 기본 str/agi(레벨 보너스 제외), 레벨, 장비 보너스 합계로 공격력/방어력을 계산한다.
        // str 1당 공격력 1, agi 2당 방어력 1 - CombatStatComponent.ApplyFromUserStats와 동일한 임시 공식이고,
        // str/agi에는 레벨 성장 보너스(StatGrowth)가 더해진다.
        public static (int AttackPower, int Defense) Calculate(int baseStr, int baseAgi, int level, int equipmentAttackBonus, int equipmentDefenseBonus)
        {
            int growth = StatGrowth.BonusAtLevel(level);
            int attackPower = baseStr + growth + equipmentAttackBonus;
            int defense = (baseAgi + growth) / 2 + equipmentDefenseBonus;
            return (attackPower, defense);
        }

        // 장착 중인 아이템들의 공격력/방어력 보너스 합계.
        public static (int Attack, int Defense) CalculateEquipmentBonus(CharacterSnapshot snapshot)
        {
            int attack = 0;
            int defense = 0;

            foreach (string itemId in snapshot.EquippedItemIds)
            {
                if (ItemCatalog.TryGet(itemId, out ItemDefinition definition))
                {
                    attack += definition.BonusAttackPower;
                    defense += definition.BonusDefense;
                }
            }

            return (attack, defense);
        }
    }
}
