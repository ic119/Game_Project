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

        // 마나. 체력과 같은 규칙이다 - DB에 저장하지 않고 최대 마나는 DB 원본(지능/레벨)으로 서버가 계산하며(CombatStatCalculator.CalculateMaxMp),
        // 입장할 때마다 가득 찬 마나로 시작하되 최근에 끊긴 상태가 있으면 이어받는다. 다른 접속자의 마나 바를 그리지는 않으므로
        // 이 값을 쓰는 건 본인 클라이언트뿐이고, 이후 변화는 Game_PlayerMpUpdate(본인에게만)로 온다.
        public int MaxMp { get; set; }
        public int CurrentMp { get; set; }
        public int AttackPower { get; set; }
        public int Defense { get; set; }

        // 경험치/레벨. 위 전투 스탯과 마찬가지로 입장 시 DB 원본으로 덮어쓰고, 이후 세션 동안의 증가분은
        // GameRoom이 몬스터 처치 시 이 인스턴스를 직접 갱신한다(ExpTable 참고). 영속화는 GameServer가
        // MainServerInternalApi로 MainServer 서버 간 API에 직접 저장한다.
        // (Nickname/HairIndex/EyeIndex/MouthIndex도 입장 시 DB 원본으로 덮어쓴다 - 클라이언트 값은 위치/맵만 쓴다.)
        public int Level { get; set; } = 1;
        public int Exp { get; set; }

        // 장착 중인 장비의 itemId(없으면 빈 문자열). 다른 접속자의 외형(무기/갑옷/투구 메시)을 그리는 데 쓰인다. 위 값들과 마찬가지로
        // 클라이언트가 채워 보낸 값은 서버가 쓰지 않고, 입장 시와 장비 변경(Game_StatUpdateRequest) 때마다 MainServer DB 원본
        // (CharacterSnapshot)으로 덮어쓴다. 바뀌면 Game_EquipmentChangedBroadcast로 주변에 알린다.
        public string WeaponItemId { get; set; } = string.Empty;
        public string ArmorItemId { get; set; } = string.Empty;
        public string HelmetItemId { get; set; } = string.Empty;

        // 서버 전용 전투 스탯 기준값. WriteTo/ReadFrom에 포함하지 않는다(네트워크로 나가지 않는다). 공격력/방어력/최대 체력은
        // "현재 레벨 + 이 기준값"으로 항상 처음부터 다시 계산한다(GameServer PlayerCombatStats) - 레벨업/장비 변경이 겹쳐도
        // 증분이 두 번 더해지거나 사라지지 않게 하기 위해서다. Game_EnterRequest/Game_StatUpdateRequest 때 DB 원본으로 채운다.
        public int BaseStr { get; set; }
        public int BaseAgi { get; set; }
        public int BaseIntel { get; set; }
        public int EquipmentAttackBonus { get; set; }
        public int EquipmentDefenseBonus { get; set; }

        // 서버 전용 마나 회복 상태(네트워크로 나가지 않는다). 매 틱 늘어나는 소수점 단위 회복량을 모아 두었다가 1 이상이 되면
        // CurrentMp에 더하고, 마지막으로 본인에게 마나를 알린 시각(UTC ticks)을 기억해 알림이 너무 자주 나가지 않게 한다.
        public float ManaRegenRemainder { get; set; }
        public long LastManaSentAtUtcTicks { get; set; }

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
            writer.Write(WeaponItemId);
            writer.Write(ArmorItemId);
            writer.Write(HelmetItemId);
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

        public byte[] Encode() => BinaryPacket.Write(WriteTo);

        public static PlayerInfo Decode(byte[] body) => BinaryPacket.Read(body, ReadFrom);
    }
}
