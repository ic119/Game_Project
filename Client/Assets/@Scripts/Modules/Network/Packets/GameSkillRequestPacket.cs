namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SSkillRequest.cs와 형식이 동일해야 한다.
    // 액티브 스킬 사용 요청. 슬롯(숫자 키 1~4)과 시전 방향(yaw, 도)만 보낸다 - 판정은 모두 서버가 한다.
    public class GameSkillRequestPacket
    {
        public long PlayerId;
        public int Slot;
        public float RotationY;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(Slot);
            writer.Write(RotationY);
        });
    }
}
