namespace Shared.Networking.Packets
{
    // 방 전체(BroadcastToAll)에 보낸다 - 골드/아이템 지급과 달리 "뚜껑이 열렸다"는 시각 정보는 본인만 알
    // 필요가 없다. 골드/아이템은 이 패킷에 담지 않는다(Game_LootBroadcast가 개봉한 본인에게만 별도로 간다) -
    // 다른 플레이어가 남의 전리품 내용을 알 필요는 없기 때문이다. 방에 새로 입장/맵 이동한 플레이어에게는
    // GameRoom.GetOpenedChestIds로 이미 열린 상자들을 즉시 같은 패킷으로 따라잡아준다.
    public class S2CChestOpenBroadcast
    {
        public string ChestId { get; set; } = string.Empty;

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ChestId);
        });

        public static S2CChestOpenBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CChestOpenBroadcast
        {
            ChestId = reader.ReadString()
        });
    }
}
