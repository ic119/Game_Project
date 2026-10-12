namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterDamageBroadcast.cs와 형식이 동일해야 한다.
    // RemainingHp는 GameServer가 이미 공격력-방어력을 계산해 보낸 최종값이다 - 플레이어 간 전투(Damage만
    // 오고 클라이언트가 로컬 Defense로 계산)와 달리, 여기서는 수신측이 그대로 덮어쓰기만 하면 된다.
    public class GameMonsterDamageBroadcastPacket
    {
        public long MonsterId;
        public long AttackerId;
        public int Damage;
        public int RemainingHp;
        public long Timestamp;

        public static GameMonsterDamageBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMonsterDamageBroadcastPacket
        {
            MonsterId = reader.ReadInt64(),
            AttackerId = reader.ReadInt64(),
            Damage = reader.ReadInt32(),
            RemainingHp = reader.ReadInt32(),
            Timestamp = reader.ReadInt64()
        });
    }
}
