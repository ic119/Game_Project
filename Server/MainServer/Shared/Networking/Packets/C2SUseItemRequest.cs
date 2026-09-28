namespace Shared.Networking.Packets
{
    // 인벤토리의 소비 아이템(물약 등) 사용 요청. 클라이언트는 사용 의사(어떤 아이템인지)만 보내고,
    // 효과(회복량) 계산과 아이템 차감은 GameServer가 한다 - 결과는 요청자에게 S2CUseItemResult로,
    // 바뀐 체력은 방 전체에 S2CPlayerHpBroadcast로 온다.
    public class C2SUseItemRequest
    {
        public long PlayerId { get; set; }
        public string ItemId { get; set; } = string.Empty;

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(ItemId);
        });

        public static C2SUseItemRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SUseItemRequest
        {
            PlayerId = reader.ReadInt64(),
            ItemId = reader.ReadString()
        });
    }
}
