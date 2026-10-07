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

        // 전투 관련 값. Game_EnterRequest 시점에 자신의 PlayerCharacterModel 기준으로 채워 보내지만, 서버는 이 값을
        // 쓰지 않고 DB 원본으로 다시 계산해 덮어쓴다(닉네임/외형/레벨/경험치도 동일 - 서버가 쓰는 건 위치/맵뿐이다).
        // 다른 접속자 정보(Game_EnterAck/Game_PlayerJoined)로 받은 값은 서버가 계산한 값이며,
        // 데미지의 방어력 차감은 각 클라이언트가 이 값(특히 Defense)으로 로컬 계산한다.
        public int MaxHp;
        public int CurrentHp;

        // 마나. 체력과 같은 규칙으로 서버가 DB 원본(지능/레벨)으로 계산해 입장 때 채워 보낸다(Game_EnterAck의 Self 등). 마나 바는 본인
        // 화면에만 있어서 다른 접속자의 값은 쓰지 않는다. 이후의 변화는 Game_PlayerMpUpdate(본인에게만)로 온다.
        public int MaxMp;
        public int CurrentMp;
        public int AttackPower;
        public int Defense;

        // 경험치/레벨. Game_EnterRequest 시점에 세이브 데이터 기준으로 채워 보내지만 서버는 DB 원본을 쓴다(위 전투 스탯과 동일).
        // 서버가 몬스터 처치로 갱신한 최신값은 Game_ExpGainBroadcast로 돌려받는다(이 필드 자체는 재전송하지 않음).
        public int Level = 1;
        public int Exp;

        // 장착 중인 장비의 itemId(없으면 빈 문자열). 다른 접속자의 외형(무기/갑옷/투구 메시)을 그리는 데 쓴다.
        // Game_EnterRequest에는 비워 보내도 된다(서버는 DB 원본으로 채운다). 이후 장비가 바뀌면 Game_EquipmentChangedBroadcast로 온다.
        public string WeaponItemId = string.Empty;
        public string ArmorItemId = string.Empty;
        public string HelmetItemId = string.Empty;

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
            writer.Write(MaxMp);
            writer.Write(CurrentMp);
            writer.Write(AttackPower);
            writer.Write(Defense);
            writer.Write(Level);
            writer.Write(Exp);
            writer.Write(WeaponItemId ?? string.Empty);
            writer.Write(ArmorItemId ?? string.Empty);
            writer.Write(HelmetItemId ?? string.Empty);
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
                MaxMp = reader.ReadInt32(),
                CurrentMp = reader.ReadInt32(),
                AttackPower = reader.ReadInt32(),
                Defense = reader.ReadInt32(),
                Level = reader.ReadInt32(),
                Exp = reader.ReadInt32(),
                WeaponItemId = reader.ReadString(),
                ArmorItemId = reader.ReadString(),
                HelmetItemId = reader.ReadString()
            };
        }

        public byte[] Encode() => GameBinaryPacket.Write(WriteTo);

        public static GamePlayerInfo Decode(byte[] body) => GameBinaryPacket.Read(body, ReadFrom);
    }
}
