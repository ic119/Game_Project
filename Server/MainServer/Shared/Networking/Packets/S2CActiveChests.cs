using System.Collections.Generic;

namespace Shared.Networking.Packets
{
    // 방에 지금 서 있는 상자 하나. Id는 클라이언트 TreasureChestInteractionController.chestId와 같은 값이다
    // (고정 상자는 씬에 이미 있는 오브젝트의 id, 후보에서 뽑힌 상자는 후보 마커 GameObject 이름).
    public class ActiveChestInfo
    {
        public string Id { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        // Drops/DropTables.json 키(등급). 클라이언트가 등급별 외형을 고르는 데 쓸 수 있다 - 드롭 내용은 담지 않는다.
        public string LootTableKey { get; set; } = string.Empty;
    }

    // 개인에게 보낸다(Game_EnterAck/Game_MapChangeAck 직후). 상자는 몬스터/플레이어처럼 관심 영역으로 거르지 않고
    // 방 전체 목록을 그대로 보낸다 - 맵당 소수뿐이라 부담이 없다. 방이 살아 있는 동안 목록이 바뀌지 않으므로
    // (리스폰 없음) 별도의 스폰/디스폰 브로드캐스트는 두지 않는다.
    public class S2CActiveChests
    {
        public List<ActiveChestInfo> Chests { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(Chests.Count);
            foreach (ActiveChestInfo chest in Chests)
            {
                writer.Write(chest.Id);
                writer.Write(chest.X);
                writer.Write(chest.Y);
                writer.Write(chest.Z);
                writer.Write(chest.LootTableKey);
            }
        });

        public static S2CActiveChests Decode(byte[] body) => BinaryPacket.Read(body, reader =>
        {
            int count = reader.ReadInt32();
            var chests = new List<ActiveChestInfo>(count);
            for (int i = 0; i < count; i++)
            {
                chests.Add(new ActiveChestInfo
                {
                    Id = reader.ReadString(),
                    X = reader.ReadSingle(),
                    Y = reader.ReadSingle(),
                    Z = reader.ReadSingle(),
                    LootTableKey = reader.ReadString()
                });
            }

            return new S2CActiveChests { Chests = chests };
        });
    }
}
