namespace Shared.Networking.Packets
{
    // 사망한 플레이어가 부활했을 때 방 전체에 보낸다(체력은 가득 찬 상태). 부활 방식은 패킷에 담지 않는다 - 좌표로 충분하다.
    // 마을 부활(선택 또는 제한 시간 초과)이면 서버가 맵 데이터(MapData/{mapId}.json의 RespawnPoint)로, 제자리 부활이면
    // 쓰러진 위치 그대로 정한다 - 본인 클라이언트는 그 좌표로 옮기고, 다른 클라이언트도 해당 원격 캐릭터를 그 좌표로 옮긴다.
    public class S2CPlayerRevived
    {
        public long PlayerId { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(CurrentHp);
            writer.Write(MaxHp);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        });

        public static S2CPlayerRevived Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CPlayerRevived
        {
            PlayerId = reader.ReadInt64(),
            CurrentHp = reader.ReadInt32(),
            MaxHp = reader.ReadInt32(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle()
        });
    }
}
