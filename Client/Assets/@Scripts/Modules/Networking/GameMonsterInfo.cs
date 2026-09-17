using System.IO;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/MonsterInfo.cs와 필드 순서가 반드시 일치해야 한다.
    // Game_EnterAck/Game_MapChangeAck의 ExistingMonsters 항목으로도, Game_MonsterSpawnBroadcast의
    // 바디로도 재사용된다.
    public class GameMonsterInfo
    {
        public long MonsterId;
        public string MonsterType = string.Empty;
        public int MaxHp;
        public int CurrentHp;
        public int AttackPower;
        public int Defense;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        public static GameMonsterInfo ReadFrom(BinaryReader reader)
        {
            return new GameMonsterInfo
            {
                MonsterId = reader.ReadInt64(),
                MonsterType = reader.ReadString(),
                MaxHp = reader.ReadInt32(),
                CurrentHp = reader.ReadInt32(),
                AttackPower = reader.ReadInt32(),
                Defense = reader.ReadInt32(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                RotationY = reader.ReadSingle()
            };
        }
    }
}
