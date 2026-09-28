namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerRevived.cs와 형식이 동일해야 한다.
    // 사망한 플레이어가 자동 부활했을 때 방 전체에 온다. 부활 위치는 본인 클라이언트가 RespawnPoint로 옮겨
    // 이동 패킷으로 알리므로 여기에는 없다.
    public class GamePlayerRevivedPacket
    {
        public long PlayerId;
        public int CurrentHp;
        public int MaxHp;

        public static GamePlayerRevivedPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GamePlayerRevivedPacket
        {
            PlayerId = reader.ReadInt64(),
            CurrentHp = reader.ReadInt32(),
            MaxHp = reader.ReadInt32()
        });
    }
}
