namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SChestOpenRequest.cs와 형식이 동일해야 한다.
    public class GameChestOpenRequestPacket
    {
        public string ChestId;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(ChestId);
        });
    }
}
