namespace Shared.Networking.Packets
{
    // 액티브 스킬 사용 요청(C2S). 슬롯(숫자 키 1~4)과 시전 방향만 보낸다 - 어떤 스킬인지는 서버가 장착 무기 종류와 슬롯으로 정하고,
    // 해금 레벨/쿨다운/마나/범위/피해는 모두 서버가 판정한다. 위치도 보내지 않는다(서버가 아는 최신 위치 기준).
    // RotationY는 Unity yaw(도)이며, 범위 판정의 방향으로 쓰인다.
    public class C2SSkillRequest
    {
        public long PlayerId { get; set; }
        public int Slot { get; set; }
        public float RotationY { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(Slot);
            writer.Write(RotationY);
        });

        public static C2SSkillRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SSkillRequest
        {
            PlayerId = reader.ReadInt64(),
            Slot = reader.ReadInt32(),
            RotationY = reader.ReadSingle()
        });
    }
}
