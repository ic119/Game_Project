namespace Shared.Networking.Packets
{
    // 방 전체에 보낸다. 리스폰으로 새 상자가 어느 후보 지점에 생겼는지 알린다(Game_ChestSpawnBroadcast).
    public class S2CChestSpawnBroadcast
    {
        public ActiveChestInfo Chest { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer => Chest.WriteTo(writer));

        public static S2CChestSpawnBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CChestSpawnBroadcast
        {
            Chest = ActiveChestInfo.ReadFrom(reader)
        });
    }
}
