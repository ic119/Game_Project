namespace GameServer.Monsters
{
    // 보스가 지금 어느 체력 구간인지, 다음에 어떤 스킬을 쓸지 정하는 순수 로직. GameRoom(전투 상태)과 분리해 두어
    // 난수원을 주입해 선택 규칙을 결정적으로 테스트할 수 있게 한다.
    public static class BossPatternPlanner
    {
        // 현재 체력 비율(%)이 속한 구간의 번호(Phases 인덱스)를 돌려준다. 체력이 BelowHpPercent 이하인 구간 중 가장 뒤(가장 낮은 기준)의 것이다.
        // 예: 구간 기준이 [100, 70, 40]이면 체력 100%=0번, 70%=1번(70% 이하), 40%=2번, 39%=2번.
        public static int PhaseIndexFor(BossPatternDefinition pattern, int currentHp, int maxHp)
        {
            // 올림으로 비율을 구해 체력이 조금이라도 남아 있으면 0%가 되지 않게 한다(1/4200도 1%).
            int hpPercent = maxHp <= 0 ? 0 : (int)Math.Ceiling(currentHp * 100.0 / maxHp);

            int index = 0;
            for (int i = 0; i < pattern.Phases.Count; i++)
            {
                if (hpPercent <= pattern.Phases[i].BelowHpPercent)
                {
                    index = i;
                }
            }

            return index;
        }

        // 구간(phaseIndex)에서 지금 쓸 수 있는 스킬 중 하나를 무작위로 고른다. 쓸 수 있는 스킬이 없으면 null.
        //  - isOnCooldown: 스킬 이름 -> 아직 재사용 대기 중인가.
        //  - distanceToTarget: 보스와 추적 대상 사이의 거리(m).
        //  - aliveSummons: 이 보스가 지금 거느린 하수인 수.
        public static string? PickSkill(BossPatternDefinition pattern, int phaseIndex, Func<string, bool> isOnCooldown,
            float distanceToTarget, int aliveSummons, Random random)
        {
            var eligible = new List<string>();
            foreach (string name in pattern.Phases[phaseIndex].Skills)
            {
                if (!pattern.Skills.TryGetValue(name, out BossSkillDefinition? skill) || isOnCooldown(name))
                {
                    continue;
                }

                bool inRange = skill.Type switch
                {
                    BossSkillType.AreaSlam => distanceToTarget <= skill.TriggerRange,
                    BossSkillType.Charge => distanceToTarget >= skill.MinRange && distanceToTarget <= skill.MaxRange,

                    // 소환은 하수인이 최대 수보다 적을 때만 한다(이미 가득하면 의미 없는 시전을 하지 않는다).
                    BossSkillType.Summon => aliveSummons < skill.MaxAlive,
                    _ => false
                };

                if (inRange)
                {
                    eligible.Add(name);
                }
            }

            return eligible.Count == 0 ? null : eligible[random.Next(eligible.Count)];
        }

        // 점 (px, pz)에서 선분 (ax, az)-(bx, bz)까지의 최단 거리. 돌진하는 보스가 지나간 선분과 플레이어의 거리 판정에 쓴다.
        public static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float abx = bx - ax;
            float abz = bz - az;
            float lengthSquared = abx * abx + abz * abz;

            // 선분이 한 점이면(이번 틱에 움직이지 않음) 그 점까지의 거리다.
            float t = lengthSquared <= 1e-9f ? 0f : Math.Clamp(((px - ax) * abx + (pz - az) * abz) / lengthSquared, 0f, 1f);
            float cx = ax + abx * t;
            float cz = az + abz * t;
            return MathF.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz));
        }
    }
}
