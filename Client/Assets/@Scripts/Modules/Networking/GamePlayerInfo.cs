using System.IO;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/PlayerInfo.cs와 필드 순서가 반드시 일치해야 한다.
    // Game_EnterRequest(C2S)의 바디로도, Game_EnterAck/Game_PlayerJoined 안의 항목으로도 재사용된다.
    public class GamePlayerInfo
    {
        public long PlayerId;
        public string Nickname = string.Empty;

        // 이 플레이어가 현재 속한 맵(서버 GameRoom 라우팅 키). MapPortalController가 맵을 옮길 때마다 갱신된다.
        public string MapId = string.Empty;
        public int HairIndex;
        public int EyeIndex;
        public int MouthIndex;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        // 전투 관련 값. Game_EnterRequest 시점에 자신의 PlayerCharacterModel(이미 ApplyUserSaveData로
        // 초기화됨) 기준으로 채워 보낸다. 서버는 이 값을 그대로 신뢰/중계만 하고(Nickname과 동일한 신뢰 수준),
        // 데미지의 방어력 차감은 각 클라이언트가 이 값(특히 Defense)으로 로컬 계산한다.
        public int MaxHp;
        public int CurrentHp;
        public int AttackPower;
        public int Defense;

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
        }

        public static GamePlayerInfo ReadFrom(BinaryReader reader)
        {
            return new GamePlayerInfo
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
                Defense = reader.ReadInt32()
            };
        }

        public byte[] Encode() => GameBinaryPacket.Write(WriteTo);

        public static GamePlayerInfo Decode(byte[] body) => GameBinaryPacket.Read(body, ReadFrom);
    }
}
