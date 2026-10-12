namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterAttackBroadcast.cs와 형식이 동일해야 한다.
    // GameDamageBroadcastPacket(PvP)와 동일하게 Damage는 최종 피해량, RemainingHp는 서버 기준 남은 체력이다.
    public class GameMonsterAttackBroadcastPacket
    {
        public long MonsterId;
        public long TargetPlayerId;
        public int Damage;
        public int RemainingHp;
        public long Timestamp;

        public static GameMonsterAttackBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterAttackBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            RemainingHp = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
