namespace Shared.Networking.Packets
{
    // 같은 접속을 유지한 채(재접속 없이) 다른 맵으로 이동할 때 보낸다.
    // 서버는 이 요청을 받으면 기존 맵의 GameRoom에서 빼서 새 맵의 GameRoom으로 옮긴다.
    public class C2SMapChangeRequest
    {
        public long PlayerId { get; set; }
        public string MapId { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(MapId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        });

        public static C2SMapChangeRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SMapChangeRequest
        {
            PlayerId = reader.ReadInt64(),
            MapId = reader.ReadString(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle()
        });
    }
}
