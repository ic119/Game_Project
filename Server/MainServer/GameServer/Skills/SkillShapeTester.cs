namespace GameServer.Skills
{
    // 스킬 범위 안에 대상이 있는지 판정한다. 방향은 Unity yaw(도): 앞 = (sin, cos). 대상은 점이 아니라 몸집이 있으므로
    // 모든 거리 비교에 TargetRadius만큼 여유를 준다.
    public static class SkillShapeTester
    {
        public const float TargetRadius = 0.5f;

        // 대상이 범위 안이면 true. 대상 정렬(가까운 순)에 쓰도록 시전자와의 거리 제곱을 함께 돌려준다.
        public static bool TryHit(SkillDefinition skill, float originX, float originZ, float rotationYDegrees, float targetX, float targetZ, out float distanceSquared)
        {
            float radians = rotationYDegrees * MathF.PI / 180f;
            float forwardX = MathF.Sin(radians);
            float forwardZ = MathF.Cos(radians);

            float dx = targetX - originX;
            float dz = targetZ - originZ;
            distanceSquared = dx * dx + dz * dz;

            float along = dx * forwardX + dz * forwardZ;
            float side = MathF.Abs(dx * forwardZ - dz * forwardX);

            switch (skill.ShapeKind)
            {
                case SkillShape.Line:
                    return along >= -TargetRadius && along <= skill.Length + TargetRadius && side <= skill.Width * 0.5f + TargetRadius;

                case SkillShape.Circle:
                {
                    float cx = dx - forwardX * skill.ForwardOffset;
                    float cz = dz - forwardZ * skill.ForwardOffset;
                    float reach = skill.Radius + TargetRadius;
                    return cx * cx + cz * cz <= reach * reach;
                }

                case SkillShape.Cone:
                {
                    float reach = skill.Length + TargetRadius;
                    if (distanceSquared > reach * reach)
                    {
                        return false;
                    }

                    // 아주 가까우면(몸이 겹침) 방향과 무관하게 맞는다.
                    if (distanceSquared <= TargetRadius * TargetRadius)
                    {
                        return true;
                    }

                    float distance = MathF.Sqrt(distanceSquared);
                    return along / distance >= MathF.Cos(skill.Angle * 0.5f * MathF.PI / 180f);
                }

                default:
                    return false;
            }
        }
    }
}
