namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerJoined.cs와 형식이 동일해야 한다.
    public class GamePlayerJoinedPacket
    {
        public GamePlayerInfo Player = new();

        public static GamePlayerJoinedPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
            new GamePlayerJoinedPacket { Player = GamePlayerInfo.ReadFrom(reader) });
    }
}
