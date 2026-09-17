using System.IO;

namespace Shared.Networking.Packets
{
    // Game_EnterAck/Game_MapChangeAck의 ExistingMonsters 항목으로도, Game_MonsterSpawnBroadcast의
    // 바디로도 재사용된다. PlayerInfo와 달리 MapId를 갖지 않는다 - 몬스터는 GameRoom(맵)에 종속되어
    // 생성/파괴되므로 어느 방 소속인지는 이미 그 방을 통해 전달된다는 것으로 알 수 있다.
    public class MonsterInfo
    {
        public long MonsterId { get; set; }
        public string MonsterType { get; set; } = string.Empty;
        public int MaxHp { get; set; }
        public int CurrentHp { get; set; }
        public int AttackPower { get; set; }
        public int Defense { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(MonsterId);
            writer.Write(MonsterType);
            writer.Write(MaxHp);
            writer.Write(CurrentHp);
            writer.Write(AttackPower);
            writer.Write(Defense);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        }

        public static MonsterInfo ReadFrom(BinaryReader reader)
        {
            return new MonsterInfo
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

        public byte[] Encode() => BinaryPacket.Write(WriteTo);

        public static MonsterInfo Decode(byte[] body) => BinaryPacket.Read(body, ReadFrom);
    }
}
