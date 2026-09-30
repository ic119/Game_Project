namespace Shared.Networking.Packets
{
    // 방 전체에 보낸다. 열린 뒤 잔존 시간이 지난 상자가 사라졌음을 알린다(Game_ChestDespawnBroadcast).
    // 클라이언트는 이 id의 상자와 "열림" 기록을 함께 지워야 한다 - 같은 id의 상자가 나중에 다시 생기면 닫힌 상태여야 하기 때문이다.
    public class S2CChestDespawnBroadcast
    {
        public string ChestId { get; set; } = string.Empty;

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ChestId);
        });

        public static S2CChestDespawnBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CChestDespawnBroadcast
        {
            ChestId = reader.ReadString()
        });
    }
}
