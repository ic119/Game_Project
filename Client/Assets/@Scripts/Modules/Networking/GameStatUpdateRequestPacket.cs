namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SStatUpdateRequest.cs와 형식이 동일해야 한다.
    public class GameStatUpdateRequestPacket
    {
        public long PlayerId;
        public int AttackPower;
        public int Defense;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(AttackPower);
            writer.Write(Defense);
        });
    }
}
