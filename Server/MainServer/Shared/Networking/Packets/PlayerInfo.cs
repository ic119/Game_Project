using System.IO;

namespace Shared.Networking.Packets
{
    // Game_EnterRequest(C2S)의 바디로도, Game_EnterAck/Game_PlayerJoined 안의 항목으로도 재사용된다.
    public class PlayerInfo
    {
        public long PlayerId { get; set; }
        public string Nickname { get; set; } = string.Empty;

        // 이 플레이어가 현재 속한 맵(GameRoom 라우팅 키). GameRoom을 맵별로 분리하는 기준값이다.
        public string MapId { get; set; } = string.Empty;
        public int HairIndex { get; set; }
        public int EyeIndex { get; set; }
        public int MouthIndex { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }

        // 전투 관련 값. Game_EnterRequest로 클라이언트가 채워 보내는 값은 서버가 쓰지 않는다 - ClientSession이
        // PlayerAuthValidator로 MainServer에서 받아온 DB 원본(CharacterSnapshot)으로 CombatStatCalculator가
        // 계산한 값으로 항상 덮어쓴다(치트 방지). AttackPower/Defense는 Game_StatUpdateRequest 때도 같은 방식으로
        // 다시 계산하고, MaxHp/CurrentHp는 입장할 때마다 가득 찬 체력으로 시작한다.
        public int MaxHp { get; set; }
        public int CurrentHp { get; set; }
        public int AttackPower { get; set; }
        public int Defense { get; set; }

        // 경험치/레벨. 위 전투 스탯과 마찬가지로 입장 시 DB 원본으로 덮어쓰고, 이후 세션 동안의 증가분은
        // GameRoom이 몬스터 처치 시 이 인스턴스를 직접 갱신한다(ExpTable 참고). 영속화는 GameServer가
        // MainServerInternalApi로 MainServer 서버 간 API에 직접 저장한다.
        // (Nickname/HairIndex/EyeIndex/MouthIndex도 입장 시 DB 원본으로 덮어쓴다 - 클라이언트 값은 위치/맵만 쓴다.)
        public int Level { get; set; } = 1;
        public int Exp { get; set; }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(PlayerId);
            writer.Write(Nickname);
            writer.Write(MapId);
            writer.Write(HairIndex);
            writer.Write(EyeIndex);
            writer.Write(MouthIndex);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
            writer.Write(MaxHp);
            writer.Write(CurrentHp);
            writer.Write(AttackPower);
            writer.Write(Defense);
            writer.Write(Level);
            writer.Write(Exp);
        }

        public static PlayerInfo ReadFrom(BinaryReader reader)
        {
            return new PlayerInfo
            {
                PlayerId = reader.ReadInt64(),
                Nickname = reader.ReadString(),
                MapId = reader.ReadString(),
                HairIndex = reader.ReadInt32(),
                EyeIndex = reader.ReadInt32(),
                MouthIndex = reader.ReadInt32(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                RotationY = reader.ReadSingle(),
                MaxHp = reader.ReadInt32(),
                CurrentHp = reader.ReadInt32(),
                AttackPower = reader.ReadInt32(),
                Defense = reader.ReadInt32(),
                Level = reader.ReadInt32(),
                Exp = reader.ReadInt32()
            };
        }

        public byte[] Encode() => BinaryPacket.Write(WriteTo);

        public static PlayerInfo Decode(byte[] body) => BinaryPacket.Read(body, ReadFrom);
    }
}
