namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerLeft.cs와 형식이 동일해야 한다.
    public class GamePlayerLeftPacket
    {
        public long PlayerId;

        public static GamePlayerLeftPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
            new GamePlayerLeftPacket { PlayerId = reader.ReadInt64() });
    }
}
