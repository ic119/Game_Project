namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerMpUpdate.cs와 형식이 동일해야 한다.
    // 본인의 마나가 바뀌었을 때(자연 회복, 레벨업/부활로 가득 참, 스킬 사용 소모) 본인에게만 온다 - 다른 접속자에게는 알리지 않는다.
    public class GamePlayerMpUpdatePacket
    {
        public long PlayerId;
        public int CurrentMp;
        public int MaxMp;

        public static GamePlayerMpUpdatePacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GamePlayerMpUpdatePacket
        {
            PlayerId = reader.ReadInt64(),
            CurrentMp = reader.ReadInt32(),
            MaxMp = reader.ReadInt32()
        });
    }
}
