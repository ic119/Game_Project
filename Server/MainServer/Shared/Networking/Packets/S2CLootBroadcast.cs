namespace Shared.Networking.Packets
{
    // 몬스터를 처치한 플레이어 본인에게만(유니캐스트) 전송된다 - S2CExpGainBroadcast와 같은 이유로
    // 방 전체에 뿌리지 않는다. GoldGained/Items는 DropTableCatalog.Roll이 서버 권위로 계산한 결과를
    // 그대로 실어 보내며, 클라이언트는 이 값을 자기 인벤토리/골드에 더하기만 하면 된다(델타).
    public class S2CLootBroadcast
    {
        public long MonsterId { get; set; }
        public int GoldGained { get; set; }
        public List<(string ItemId, int Qty)> Items { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(GoldGained);

            writer.Write(Items.Count);
            foreach (var (itemId, qty) in Items)
            {
                writer.Write(itemId);
                writer.Write(qty);
            }
        });

        public static S2CLootBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader =>
        {
            long monsterId = reader.ReadInt64();
            int goldGained = reader.ReadInt32();

            int itemCount = reader.ReadInt32();
            var items = new List<(string ItemId, int Qty)>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                string itemId = reader.ReadString();
                int qty = reader.ReadInt32();
                items.Add((itemId, qty));
            }

            return new S2CLootBroadcast { MonsterId = monsterId, GoldGained = goldGained, Items = items };
        });
    }
}
