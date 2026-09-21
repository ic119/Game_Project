using System.Collections.Generic;

namespace Incheol.Modules.Networking
{
    /// <summary>
    /// 몬스터 처치 시 드롭된 아이템 하나. 서버 Shared/Networking/Packets/S2CLootBroadcast.cs의
    /// (ItemId, Qty) 튜플 항목과 형식이 동일해야 한다.
    /// </summary>
    public class GameLootItemEntry
    {
        public string ItemId;
        public int Qty;
    }

    // 서버 Shared/Networking/Packets/S2CLootBroadcast.cs와 형식이 동일해야 한다.
    // 몬스터를 처치한 본인에게만 오는 패킷이다(방 전체 브로드캐스트가 아님) - GameExpGainBroadcastPacket과 같은 이유.
    public class GameLootBroadcastPacket
    {
        public long MonsterId;
        public int GoldGained;
        public List<GameLootItemEntry> Items = new();

        public static GameLootBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
        {
            long monsterId = reader.ReadInt64();
            int goldGained = reader.ReadInt32();

            int itemCount = reader.ReadInt32();
            var items = new List<GameLootItemEntry>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                string itemId = reader.ReadString();
                int qty = reader.ReadInt32();
                items.Add(new GameLootItemEntry { ItemId = itemId, Qty = qty });
            }

            return new GameLootBroadcastPacket { MonsterId = monsterId, GoldGained = goldGained, Items = items };
        });
    }
}
