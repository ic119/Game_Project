namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SMonsterAttackRequest.cs와 형식이 동일해야 한다.
    public class GameMonsterAttackRequestPacket
    {
        public long AttackerId;
        public long MonsterId;
        public long Timestamp;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(AttackerId);
            writer.Write(MonsterId);
            writer.Write(Timestamp);
        });
    }
}
