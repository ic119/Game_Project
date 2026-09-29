namespace Shared.Networking.Packets
{
    // ChestId는 MapData/{mapId}.json의 chests[].id와 정확히 일치해야 한다(MapDataExporter가 내보낸 값).
    // GameRoom.TryOpenChest가 사거리/선착순 여부를 직접 검증하므로, 여기서는 위조된 ChestId를 보내도
    // 존재하지 않는 상자로 취급되어 조용히 무시된다.
    public class C2SChestOpenRequest
    {
        public string ChestId { get; set; } = string.Empty;

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ChestId);
        });

        public static C2SChestOpenRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SChestOpenRequest
        {
            ChestId = reader.ReadString()
        });
    }
}
