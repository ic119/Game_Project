namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SChatRequest.cs와 형식이 동일해야 한다.
    public class GameChatRequestPacket
    {
        public long PlayerId;
        public string Message = string.Empty;
        public long Timestamp;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(Message);
            writer.Write(Timestamp);
        });
    }
}
