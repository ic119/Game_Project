using Shared.Networking.Packets;

namespace GameServer.Combat
{
    // 접속 중인 플레이어(PlayerInfo)의 전투 스탯(공격력/방어력/최대 체력)을 갱신하는 규칙. 입장, 장비 변경, 레벨업 세 곳이 모두
    // 같은 계산(CombatStatCalculator)을 "처음부터 다시" 하도록 한 곳에 모았다.
    //
    // 레벨업이나 장비 변경이 증분(+n)으로 값을 고치면, 두 이벤트가 겹칠 때(예: 장비를 바꾸는 비동기 재조회가 끝나는 순간 레벨업)
    // 성장 보너스가 두 번 더해지거나 되돌아간다. 그래서 PlayerInfo에 서버 전용 기준값(BaseStr/BaseAgi/장비 보너스)을 두고,
    // 값이 바뀔 때마다 "현재 레벨 + 기준값"으로 절대값을 다시 계산한다. PlayerInfo.CurrentHp와 같은 이유로 PlayerInfo 인스턴스를
    // lock으로 보호한다(GameRoom.TryDamagePlayer와 같은 규칙) - 몬스터 AI 틱, 여러 세션이 동시에 만진다.
    public static class PlayerCombatStats
    {
        // 입장(Game_EnterRequest)과 장비 변경(Game_StatUpdateRequest 재조회 결과) 때: DB 스냅샷의 기본 str/agi와 장비 보너스를
        // 기준값으로 저장하고, 현재 레벨 기준으로 공격력/방어력을 다시 계산한다.
        // 레벨은 snapshot.Level(DB, 킬 보상 저장이 늦으면 이전 레벨)이 아니라 info.Level(서버 메모리, 접속 중 오른 레벨)을 쓴다 -
        // 입장 시에는 호출측이 info.Level을 snapshot.Level로 먼저 채워 둔다.
        // 최대 체력은 건드리지 않는다. 입장 때는 재접속 시 이어받은 체력과 함께 호출측이 정하고, 장비 변경으로는 바뀌지 않는다
        // (agi가 바뀌는 기능이 생기면 함께 갱신해야 한다).
        public static void ApplySnapshot(PlayerInfo info, CharacterSnapshot snapshot)
        {
            (int equipmentAttack, int equipmentDefense) = CombatStatCalculator.CalculateEquipmentBonus(snapshot);

            lock (info)
            {
                info.BaseStr = snapshot.Str;
                info.BaseAgi = snapshot.Agi;
                info.EquipmentAttackBonus = equipmentAttack;
                info.EquipmentDefenseBonus = equipmentDefense;
                Recalculate(info);
            }
        }

        // 레벨업 후: info.Level이 이미 새 레벨로 바뀐 상태에서 호출한다. 공격력/방어력/최대 체력을 새 레벨 기준으로 다시 계산하고
        // 체력을 가득 채운다(기존 레벨업 동작과 같다). 바뀐 (현재 체력, 최대 체력)을 돌려줘 호출측이 알림을 보낸다.
        public static (int CurrentHp, int MaxHp) ApplyLevelUp(PlayerInfo info)
        {
            lock (info)
            {
                Recalculate(info);
                info.MaxHp = CombatStatCalculator.CalculateMaxHp(info.BaseAgi, info.Level);
                info.CurrentHp = info.MaxHp;
                return (info.CurrentHp, info.MaxHp);
            }
        }

        // 호출측이 lock(info)을 잡고 있어야 한다.
        private static void Recalculate(PlayerInfo info)
        {
            (info.AttackPower, info.Defense) = CombatStatCalculator.Calculate(
                info.BaseStr, info.BaseAgi, info.Level, info.EquipmentAttackBonus, info.EquipmentDefenseBonus);
        }
    }
}
