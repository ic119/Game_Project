namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerHpBroadcast.cs와 형식이 동일해야 한다.
    // 피격이 아닌 이유(레벨업 등)로 플레이어 체력이 바뀌었을 때 방 전체에 온다.
    public class GamePlayerHpBroadcastPacket
    {
        public long PlayerId;
        public int CurrentHp;
        public int MaxHp;

        public static GamePlayerHpBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GamePlayerHpBroadcastPacket
        {
            PlayerId = reader.ReadInt64(),
            CurrentHp = reader.ReadInt32(),
            MaxHp = reader.ReadInt32()
        });
    }
}
