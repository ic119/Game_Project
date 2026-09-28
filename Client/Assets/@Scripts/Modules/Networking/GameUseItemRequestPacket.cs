namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SUseItemRequest.cs와 형식이 동일해야 한다.
    // 사용 의사(아이템 id)만 보낸다 - 회복량 계산과 아이템 차감은 서버가 한다.
    public class GameUseItemRequestPacket
    {
        public long PlayerId;
        public string ItemId = string.Empty;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(ItemId);
        });
    }
}
