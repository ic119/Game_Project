using System.Collections.Generic;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CBossSkillTelegraphBroadcast.cs의 BossSkillPoint와 필드 순서가 일치해야 한다.
    // 소환 스킬의 하수인이 나타날 자리 한 곳.
    public class GameBossSkillPoint
    {
        public float X;
        public float Z;
    }

    // 서버 Shared/Networking/Packets/S2CBossSkillTelegraphBroadcast.cs와 형식이 동일해야 한다.
    // 보스가 스킬을 준비하기 시작했다(예고). 바닥에 위험 범위를 DurationMs 동안 차오르게 보여주고 보스의 시전 모션을 재생한다.
    // 판정은 예고가 끝나는 순간 서버가 한다(명중: GameMonsterAttackBroadcastPacket, 회피: GameMonsterAttackDodgedBroadcastPacket).
    //
    // 스킬 종류(SkillType)별 쓰이는 필드:
    //  1 = 범위 공격: CenterX/CenterZ 중심, 반지름 Radius인 원.
    //  2 = 돌진: CenterX/CenterZ에서 RotationY 방향으로 길이 Length, 폭 Width인 직사각형.
    //  3 = 소환: Points 위치마다 하수인이 나타난다.
    public class GameBossSkillTelegraphBroadcastPacket
    {
        public long MonsterId;
        public byte SkillType;
        public float CenterX;
        public float CenterZ;

        // 몬스터 RotationY와 같은 규칙(Y축 회전, 도).
        public float RotationY;
        public float Radius;
        public float Width;
        public float Length;

        // 예고가 시작된 시점부터 판정까지의 시간(밀리초).
        public int DurationMs;

        public List<GameBossSkillPoint> Points = new();

        public static GameBossSkillTelegraphBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
        {
            var packet = new GameBossSkillTelegraphBroadcastPacket
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
                packet.Points.Add(new GameBossSkillPoint { X = reader.ReadSingle(), Z = reader.ReadSingle() });
            }

            return packet;
        });
    }
}
