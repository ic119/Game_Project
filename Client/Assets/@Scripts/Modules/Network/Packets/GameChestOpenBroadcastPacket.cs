namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CChestOpenBroadcast.cs와 형식이 동일해야 한다.
    // 골드/아이템은 담지 않는다 - 그건 개봉한 본인에게만 Game_LootBroadcast로 따로 간다. 이 패킷은
    // "이 ChestId의 뚜껑이 열렸다"는 시각 동기화 전용이라, 방에 새로 입장했을 때 이미 열린 상자를
    // 따라잡을 때도 그대로 재사용된다(TreasureChestInteractionController 입장에서는 구분할 필요가 없다).
    public class GameChestOpenBroadcastPacket
    {
        public string ChestId;

        public static GameChestOpenBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameChestOpenBroadcastPacket
        {
            ChestId = reader.ReadString()
        });
    }
}
