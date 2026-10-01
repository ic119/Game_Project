namespace Shared.Networking.Packets
{
    // 맵 이동 거부 사유. 클라이언트(Assets/@Scripts/Modules/Networking/GameMapChangeRejectedPacket.cs)와 값이 같아야 한다.
    public enum MapChangeRejectReason
    {
        None = 0,

        // 사망 중에는 맵을 옮기지 않는다(부활 예약이 사망한 방에 걸려 있다).
        Dead = 1,

        // 서버가 아는 마지막 위치 근처에 요청한 맵으로 가는 포탈이 없다.
        NotAtPortal = 2,

        // 서버에 맵 데이터가 없는 맵이다.
        UnknownMap = 3
    }

    // 맵 이동 요청(Game_MapChangeRequest)을 서버가 거부했다. 거부해도 서버 상태는 전혀 바뀌지 않는다(방도 위치도 그대로).
    // 승인은 Game_MapChangeAck(새 맵의 시야 안 플레이어/몬스터 목록)로 알린다.
    public class S2CMapChangeRejected
    {
        // 클라이언트가 요청한 맵 id. 응답이 어느 요청에 대한 것인지 맞춰 보는 데 쓴다.
        public string MapId { get; set; } = string.Empty;
        public MapChangeRejectReason Reason { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MapId);
            writer.Write((int)Reason);
        });

        public static S2CMapChangeRejected Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CMapChangeRejected
        {
            MapId = reader.ReadString(),
            Reason = (MapChangeRejectReason)reader.ReadInt32()
        });
    }
}
