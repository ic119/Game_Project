namespace Shared.Networking.Packets
{
    // 부활 선택(C2S). 사망한 플레이어가 마을 부활(맵의 RespawnPoint) 또는 제자리 부활(쓰러진 위치)을 고른다.
    // 위치는 담지 않는다 - 부활 좌표는 서버가 정한다(클라이언트 좌표를 받으면 순간이동이 된다).
    public class C2SReviveRequest
    {
        public long PlayerId { get; set; }

        // true면 제자리 부활, false면 마을 부활.
        public bool InPlace { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(InPlace);
        });

        public static C2SReviveRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SReviveRequest
        {
            PlayerId = reader.ReadInt64(),
            InPlace = reader.ReadBoolean()
        });
    }
}
