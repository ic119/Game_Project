namespace GameServer.Maps
{
    // 방이 만들어질 때 상자 후보 중 어디에 실제로 상자를 세울지 정한다. 같은 LootTableKey(등급)끼리 묶어 등급마다
    // 정해진 개수만큼 중복 없이 뽑는다(부분 Fisher-Yates 셔플 - 편향이 없고 O(뽑을 개수)).
    // 순수 함수라 GameRoom 없이 테스트할 수 있고, Random을 받으므로 시드를 고정해 결과를 재현할 수 있다.
    public static class ChestSpawnSelector
    {
        public static List<MapChest> Select(IReadOnlyList<MapChest> candidates, IReadOnlyList<MapChestSpawnCount> counts, Random random)
        {
            var selected = new List<MapChest>();

            foreach (MapChestSpawnCount spawnCount in counts)
            {
                // 원본 후보 목록은 건드리지 않는다(방마다 새로 뽑아야 하므로) - 등급별 사본만 섞는다.
                List<MapChest> pool = candidates.Where(c => c.LootTableKey == spawnCount.LootTableKey).ToList();
                int take = Math.Min(Math.Max(spawnCount.Count, 0), pool.Count);

                for (int i = 0; i < take; i++)
                {
                    int j = random.Next(i, pool.Count);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                    selected.Add(pool[i]);
                }
            }

            return selected;
        }

        // 리스폰용: 같은 LootTableKey 후보 중 지금 서 있지 않은 것 하나를 뽑는다(등급별 개수가 유지된다).
        // preferNotId(방금 열린 자리)는 다른 빈 후보가 있으면 피하고, 그것뿐이면 그 자리를 그대로 쓴다.
        // 빈 후보가 하나도 없으면 null.
        public static MapChest? PickReplacement(IReadOnlyList<MapChest> candidates, IReadOnlySet<string> activeIds, string lootTableKey, string? preferNotId, Random random)
        {
            List<MapChest> free = candidates
                .Where(c => c.LootTableKey == lootTableKey && !activeIds.Contains(c.Id))
                .ToList();

            if (free.Count == 0)
            {
                return null;
            }

            List<MapChest> preferred = free.Where(c => c.Id != preferNotId).ToList();
            List<MapChest> pool = preferred.Count > 0 ? preferred : free;
            return pool[random.Next(pool.Count)];
        }
    }
}
