namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterAttackBroadcast.cs와 형식이 동일해야 한다.
    // Damage는 GameDamageBroadcastPacket(PvP)와 동일하게 방어력 적용 전 원본 공격력이다 -
    // 수신측이 TargetPlayerId의 로컬 Defense로 직접 계산한다.
    public class GameMonsterAttackBroadcastPacket
    {
        public long MonsterId;
        public long TargetPlayerId;
        public int Damage;
        public long Timestamp;

        public static GameMonsterAttackBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterAttackBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            TargetPlayerId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
