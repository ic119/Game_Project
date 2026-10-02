namespace Shared.Networking.Packets
{
    // 개인에게 보낸다(Game_EnterAck/Game_MapChangeAck 직후, 상자 목록 다음). 방 생성 시 후보 중에서 뽑힌 던전 게이트의 위치를 알린다.
    // 클라이언트는 Id와 같은 이름의 후보 마커(DungeonGateSpawnPointMarker) 자리에 게이트 프리팹을 만든다.
    // 게이트가 없는 맵이면 HasGate가 false이고 나머지 필드는 기본값이다(항상 보내서 이전 맵의 게이트를 확실히 정리하게 한다).
    // 게이트는 방이 살아 있는 동안 위치가 바뀌지 않으므로 Spawn/Despawn 브로드캐스트는 따로 없다.
    public class S2CActiveGate
    {
        public bool HasGate { get; set; }
        public string Id { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(HasGate);
            writer.Write(Id);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
        });

        public static S2CActiveGate Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CActiveGate
        {
            HasGate = reader.ReadBoolean(),
            Id = reader.ReadString(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle()
        });
    }
}
