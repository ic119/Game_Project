namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CDamageBroadcast.cs와 형식이 동일해야 한다.
    // Damage는 방어력 적용 전 원본 공격력이다 - 수신측이 TargetId의 로컬 Defense로 직접 계산한다.
    public class GameDamageBroadcastPacket
    {
        public long AttackerId;
        public long TargetId;
        public int Damage;
        public long Timestamp;

        public static GameDamageBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameDamageBroadcastPacket
        {
            AttackerId = reader.ReadInt64(),
            TargetId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
