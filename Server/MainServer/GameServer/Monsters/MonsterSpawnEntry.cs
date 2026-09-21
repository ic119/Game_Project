namespace GameServer.Monsters
{
    // 스폰 포인트 하나가 여러 몬스터 타입을 섞어 낼 수 있게, 타입별 전투 스탯을 담는 항목.
    // 스폰/리스폰마다 소속 포인트(MonsterSpawnPointDefinition.Entries)에서 이 중 하나가 무작위로
    // 선택된다(GameRoom.SpawnMonsterAtPoint 참고) - 몬스터는 소유 클라이언트/DB가 없어 스탯의
    // 유일한 출처가 여기(서버)다.
    public class MonsterSpawnEntry
    {
        public string MonsterType { get; init; } = string.Empty;
        public int MaxHp { get; init; } = 30;
        public int AttackPower { get; init; } = 5;
        public int Defense { get; init; } = 0;

        // 처치 시 지급할 경험치. 가이드라인: round(MaxHp * 0.4 + AttackPower * 2).
        public int ExpReward { get; init; } = 20;
    }
}
