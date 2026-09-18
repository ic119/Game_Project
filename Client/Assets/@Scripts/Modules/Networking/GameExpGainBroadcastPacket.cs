namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CExpGainBroadcast.cs와 형식이 동일해야 한다.
    // 몬스터를 처치한 본인에게만 오는 패킷이다(방 전체 브로드캐스트가 아님).
    public class GameExpGainBroadcastPacket
    {
        public long MonsterId;
        public int GainedExp;
        public int TotalExp;
        public int Level;
        public bool DidLevelUp;
        public int ExpToNextLevel;

        public static GameExpGainBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameExpGainBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            GainedExp = reader.ReadInt32(),
            TotalExp = reader.ReadInt32(),
            Level = reader.ReadInt32(),
            DidLevelUp = reader.ReadBoolean(),
            ExpToNextLevel = reader.ReadInt32()
        });
    }
}
