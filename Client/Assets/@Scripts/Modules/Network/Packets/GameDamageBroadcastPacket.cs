namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CDamageBroadcast.cs와 형식이 동일해야 한다.
    // Damage는 서버가 방어력까지 적용한 최종 피해량, RemainingHp는 서버 기준 대상의 남은 체력이다 -
    // 수신측은 계산하지 않고 그대로 표시한다. RemainingHp가 0이면 이 공격으로 대상이 사망했다.
    public class GameDamageBroadcastPacket
    {
        public long AttackerId;
        public long TargetId;
        public int Damage;
        public int RemainingHp;
        public long Timestamp;

        public static GameDamageBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameDamageBroadcastPacket
        {
            AttackerId = reader.ReadInt64(),
            TargetId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            RemainingHp = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
