using GameServer.Skills;

namespace GameServer.Tests;

// 방향은 Unity yaw(도): rotation 0이면 +Z가 앞, 90이면 +X가 앞이다.
public class SkillShapeTesterTests
{
    private static SkillDefinition Line(float length = 4f, float width = 2f) => new() { Shape = "Line", Length = length, Width = width };
    private static SkillDefinition Circle(float radius = 2f, float forwardOffset = 0f) => new() { Shape = "Circle", Radius = radius, ForwardOffset = forwardOffset };
    private static SkillDefinition Cone(float length = 3f, float angle = 90f) => new() { Shape = "Cone", Length = length, Angle = angle };

    private static bool Hit(SkillDefinition skill, float rotation, float targetX, float targetZ)
        => SkillShapeTester.TryHit(skill, 0f, 0f, rotation, targetX, targetZ, out _);

    [Fact]
    public void Line_HitsInFrontWithinLengthAndWidth_AndFollowsRotation()
    {
        Assert.True(Hit(Line(), 0f, 0f, 3f));       // 정면 3m
        Assert.True(Hit(Line(), 0f, 0.9f, 3f));     // 폭 안(1 + 대상 몸집 0.5)
        Assert.False(Hit(Line(), 0f, 2f, 3f));      // 폭 밖
        Assert.False(Hit(Line(), 0f, 0f, 5f));      // 길이 밖(4 + 0.5 초과)
        Assert.False(Hit(Line(), 0f, 0f, -2f));     // 뒤쪽
        Assert.True(Hit(Line(), 90f, 3f, 0f));      // +X를 바라보면 +X가 앞
        Assert.False(Hit(Line(), 90f, 0f, 3f));
    }

    [Fact]
    public void Circle_AtSelf_HitsAllAroundRegardlessOfRotation()
    {
        Assert.True(Hit(Circle(2f), 0f, 0f, 2f));
        Assert.True(Hit(Circle(2f), 0f, 0f, -2f));
        Assert.True(Hit(Circle(2f), 123f, -1.5f, 1.5f));
        Assert.False(Hit(Circle(2f), 0f, 0f, 3f)); // 2 + 0.5 초과
    }

    [Fact]
    public void Circle_WithForwardOffset_CentersInFrontOfCaster()
    {
        SkillDefinition slam = Circle(2f, forwardOffset: 3f);

        Assert.True(Hit(slam, 0f, 0f, 3f));   // 중심
        Assert.True(Hit(slam, 0f, 0f, 5f));   // 중심에서 2m
        Assert.False(Hit(slam, 0f, 0f, 0f));  // 시전자 자리는 범위 밖
        Assert.True(Hit(slam, 90f, 3f, 0f));  // 방향을 따라 중심이 옮겨진다
        Assert.False(Hit(slam, 90f, 0f, 3f));
    }

    [Fact]
    public void Cone_HitsOnlyInsideAngleAndRange()
    {
        Assert.True(Hit(Cone(), 0f, 0f, 2f));       // 정면
        Assert.True(Hit(Cone(), 0f, 1.5f, 1.5f));   // 45도(경계 안쪽)
        Assert.False(Hit(Cone(), 0f, 2f, 0.5f));    // 약 76도 - 각도 밖
        Assert.False(Hit(Cone(), 0f, 0f, 4f));      // 사거리 밖
        Assert.False(Hit(Cone(), 0f, 0f, -2f));     // 뒤쪽
        Assert.True(Hit(Cone(), 0f, 0.1f, -0.1f));  // 몸이 겹칠 만큼 가까우면 방향 무관
    }

    [Fact]
    public void TryHit_ReportsDistanceSquaredFromOrigin()
    {
        SkillShapeTester.TryHit(Line(), 1f, 1f, 0f, 1f, 4f, out float distanceSquared);

        Assert.Equal(9f, distanceSquared, 3);
    }
}
