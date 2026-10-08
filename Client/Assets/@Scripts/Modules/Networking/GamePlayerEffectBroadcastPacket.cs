namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPlayerEffectBroadcast.cs의 PlayerEffectType과 값이 같아야 한다.
    public enum GamePlayerEffectType : byte
    {
        None = 0,
        LevelUp = 1,
        HpPotion = 2
    }

    // 서버 Shared/Networking/Packets/S2CPlayerEffectBroadcast.cs와 형식이 동일해야 한다.
    // 다른 플레이어의 캐릭터 연출(레벨업/물약 이펙트) 알림. 본인의 연출은 로컬에서 재생하므로 오지 않는다.
    // RemotePlayerManager가 해당 원격 캐릭터에 이펙트를 재생한다.
    public class GamePlayerEffectBroadcastPacket
    {
        public long PlayerId;
        public GamePlayerEffectType Effect;

        public static GamePlayerEffectBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GamePlayerEffectBroadcastPacket
        {
            PlayerId = reader.ReadInt64(),
            Effect = (GamePlayerEffectType)reader.ReadByte()
        });
    }
}
