namespace GameServer.Monsters
{
    // 스폰 포인트의 Entries 중 Weight 비율로 하나를 고른다. GameRoom에서 분리해 둔 이유는 난수원(Random)을
    // 주입해 선택 비율을 결정적으로 테스트할 수 있게 하기 위해서다(ChestSpawnSelector와 같은 방식).
    public static class MonsterSpawnSelector
    {
        public static MonsterSpawnEntry Pick(IReadOnlyList<MonsterSpawnEntry> entries, Random random)
        {
            int totalWeight = 0;
            foreach (MonsterSpawnEntry entry in entries)
            {
                totalWeight += entry.Weight;
            }

            // 0 이상 totalWeight 미만의 값이 어느 항목 구간에 들어가는지로 고른다.
            int roll = random.Next(totalWeight);
            foreach (MonsterSpawnEntry entry in entries)
            {
                if (roll < entry.Weight)
                {
                    return entry;
                }

                roll -= entry.Weight;
            }

            // Weight가 전부 1 이상이면(MonsterSpawnCatalog 로드 검증) 도달할 수 없다.
            throw new InvalidOperationException("스폰 엔트리 선택에 실패했습니다 - Weight 합계가 0 이하입니다.");
        }
    }
}
