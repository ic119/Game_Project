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

        // 전투 관련 값. 클라이언트가 Game_EnterRequest 시점에 자신의 세이브 데이터 기준으로 채워 보낸다.
        // MaxHp/CurrentHp는 Nickname과 동일한 신뢰 수준(GameServer는 DB 접근 권한이 없어 직접 검증하지 못한다)
        // 그대로 받아들이지만, AttackPower/Defense는 여기 채워진 값을 그대로 쓰지 않는다 - ClientSession이
        // Game_EnterRequest/Game_StatUpdateRequest를 처리할 때마다 PlayerAuthValidator로 MainServer에서
        // str/agi/장착 아이템을 다시 조회해 CombatStatCalculator로 직접 재계산한 값으로 항상 덮어쓴다
        // (치트 방지 - 여기 채워지는 값은 서버가 실제로 사용하기 전 임시값일 뿐이다).
        public int MaxHp { get; set; }
        public int CurrentHp { get; set; }
        public int AttackPower { get; set; }
        public int Defense { get; set; }

        // 경험치/레벨. 클라이언트가 Game_EnterRequest 시점에 자신의 세이브 데이터(DB 영속값) 기준으로
        // 채워 보내며(위 전투 스탯과 동일한 신뢰 수준), 이후 세션 동안의 증가분은 GameRoom이 몬스터 처치 시
        // 이 인스턴스를 직접 갱신한다(ExpTable 참고). 영속화는 클라이언트가 Game_ExpGainBroadcast를 받아
        // MainServer(HTTP)에 별도로 저장한다 - GameServer 자체는 DB 접근 권한이 없다.
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
