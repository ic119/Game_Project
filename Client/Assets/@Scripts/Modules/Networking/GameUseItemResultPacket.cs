namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CUseItemResult.cs와 형식이 동일해야 한다.
    // Success면 서버에서 아이템 1개가 실제로 차감됐다는 뜻이라 이때만 인벤토리 수량을 줄인다.
    // 회복된 체력은 이 패킷이 아니라 Game_PlayerHpBroadcast로 따로 온다.
    public class GameUseItemResultPacket
    {
        public string ItemId;
        public bool Success;

        public static GameUseItemResultPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameUseItemResultPacket
        {
            ItemId = reader.ReadString(),
            Success = reader.ReadBoolean()
        });
    }
}
