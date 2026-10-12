namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterDieBroadcast.cs와 형식이 동일해야 한다.
    public class GameMonsterDieBroadcastPacket
    {
        public long MonsterId;
        public long Timestamp;

        public static GameMonsterDieBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterDieBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            Timestamp = reader.ReadInt64()
        });
    }
}
