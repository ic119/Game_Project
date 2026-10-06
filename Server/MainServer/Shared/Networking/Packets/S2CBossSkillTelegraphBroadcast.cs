namespace Shared.Networking.Packets
{
    // 소환 스킬의 하수인이 나타날 자리 한 곳. S2CBossSkillTelegraphBroadcast.Points의 항목이다.
    public class BossSkillPoint
    {
        public float X { get; set; }
        public float Z { get; set; }
    }

    // 보스가 스킬을 준비하기 시작했다(예고). 클라이언트는 이 알림으로 바닥에 위험 범위를 DurationMs 동안 차오르게 보여주고
    // 보스의 시전 모션을 재생한다 - 예고가 끝나는 순간 서버가 판정하므로 플레이어는 그 전에 범위 밖으로 벗어나거나 대쉬로 피할 수 있다.
    // 판정 결과는 기존 알림을 그대로 쓴다: 명중은 Game_MonsterAttackBroadcast, 대쉬 회피는 Game_MonsterAttackDodgedBroadcast.
    // 예고가 끝나면(또는 시전이 취소되면) S2CBossSkillEndBroadcast가 온다.
    //
    // 스킬 종류(SkillType)별 쓰이는 필드:
    //  1 = 범위 공격(AreaSlam): CenterX/CenterZ를 중심으로 반지름 Radius인 원.
    //  2 = 돌진(Charge): CenterX/CenterZ에서 RotationY 방향으로 길이 Length, 폭 Width인 직사각형(보스는 이 방향으로 달려간다).
    //  3 = 소환(Summon): Points 위치마다 하수인이 나타난다.
    public class S2CBossSkillTelegraphBroadcast
    {
        public long MonsterId { get; set; }
        public byte SkillType { get; set; }
        public float CenterX { get; set; }
        public float CenterZ { get; set; }

        // 몬스터 RotationY와 같은 규칙(Atan2(dx, dz)를 도로 바꾼 값, Y축 회전).
        public float RotationY { get; set; }
        public float Radius { get; set; }
        public float Width { get; set; }
        public float Length { get; set; }

        // 예고가 시작된 시점부터 판정까지의 시간(밀리초). 클라이언트의 차오르는 연출 길이와 같다.
        public int DurationMs { get; set; }

        public List<BossSkillPoint> Points { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(SkillType);
            writer.Write(CenterX);
            writer.Write(CenterZ);
            writer.Write(RotationY);
            writer.Write(Radius);
            writer.Write(Width);
            writer.Write(Length);
            writer.Write(DurationMs);
            writer.Write(Points.Count);
            foreach (BossSkillPoint point in Points)
            {
                writer.Write(point.X);
                writer.Write(point.Z);
            }
        });

        public static S2CBossSkillTelegraphBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader =>
        {
            var packet = new S2CBossSkillTelegraphBroadcast
            {
                MonsterId = reader.ReadInt64(),
                SkillType = reader.ReadByte(),
                CenterX = reader.ReadSingle(),
                CenterZ = reader.ReadSingle(),
                RotationY = reader.ReadSingle(),
                Radius = reader.ReadSingle(),
                Width = reader.ReadSingle(),
                Length = reader.ReadSingle(),
                DurationMs = reader.ReadInt32()
            };

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                packet.Points.Add(new BossSkillPoint { X = reader.ReadSingle(), Z = reader.ReadSingle() });
            }

            return packet;
        });
    }
}
