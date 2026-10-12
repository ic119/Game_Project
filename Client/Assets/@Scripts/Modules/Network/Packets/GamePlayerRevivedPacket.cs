namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerRevived.cs와 형식이 동일해야 한다.
    // 사망한 플레이어가 자동 부활했을 때 방 전체에 온다. 부활 위치(X/Y/Z/RotationY)는 서버가 맵 데이터로
    // 정한 좌표라, 본인/원격 모두 그 좌표로 바로 옮긴다.
    public class GamePlayerRevivedPacket
    {
        public long PlayerId;
        public int CurrentHp;
        public int MaxHp;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        public static GamePlayerRevivedPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GamePlayerRevivedPacket
        {
            PlayerId = reader.ReadInt64(),
            CurrentHp = reader.ReadInt32(),
            MaxHp = reader.ReadInt32(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle()
        });
    }
}
