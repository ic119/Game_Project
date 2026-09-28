namespace Shared.Networking.Packets
{
    // C2SUseItemRequest의 처리 결과(요청자 본인에게만). Success면 아이템 1개가 실제로 차감됐다는 뜻이라
    // 클라이언트는 이때만 인벤토리 수량을 줄인다. 실패(사망/만피/회복 아이템 아님/미보유/저장 실패)면 아무 것도 바뀌지 않았다.
    public class S2CUseItemResult
    {
        public string ItemId { get; set; } = string.Empty;
        public bool Success { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ItemId);
            writer.Write(Success);
        });

        public static S2CUseItemResult Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CUseItemResult
        {
            ItemId = reader.ReadString(),
            Success = reader.ReadBoolean()
        });
    }
}
