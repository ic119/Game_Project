namespace GameServer.Monsters
{
    // 스폰 포인트 몬스터가 활동할 수 있는 XZ 평면의 직사각형 영역(월드 좌표, 중심 + 전체 크기).
    // 서버 몬스터 이동에는 벽 충돌/내비메시가 없어서(GameRoom.MoveToward는 직선 이동), 방과 복도로 이루어진 던전에서는
    // 몬스터가 옆방 플레이어를 감지해 벽을 뚫고 달려올 수 있다 - 영역을 방 하나로 지정하면 몬스터는 그 방 안의
    // 플레이어만 감지/추적하고 방 밖으로 나가지 않는다. 영역이 없는 포인트(트인 필드)는 기존 동작 그대로다.
    public class SpawnArea
    {
        public float CenterX { get; init; }
        public float CenterZ { get; init; }
        public float SizeX { get; init; }
        public float SizeZ { get; init; }

        // 크기가 0 이하면 "영역 없음"으로 취급한다 - Unity JsonUtility는 null 필드를 내보낼 수 없어,
        // 영역을 쓰지 않는 마커는 크기 0의 영역으로 내보내진다.
        public bool IsDefined => SizeX > 0f && SizeZ > 0f;

        public bool Contains(float x, float z)
        {
            return MathF.Abs(x - CenterX) <= SizeX / 2f && MathF.Abs(z - CenterZ) <= SizeZ / 2f;
        }

        // 영역 밖의 좌표를 가장 가까운 영역 안 좌표로 끌어온다.
        public (float X, float Z) Clamp(float x, float z)
        {
            float halfX = SizeX / 2f;
            float halfZ = SizeZ / 2f;
            return (Math.Clamp(x, CenterX - halfX, CenterX + halfX),
                    Math.Clamp(z, CenterZ - halfZ, CenterZ + halfZ));
        }
    }
}
