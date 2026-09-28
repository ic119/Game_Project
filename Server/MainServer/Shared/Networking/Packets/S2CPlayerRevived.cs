namespace Shared.Networking.Packets
{
    // 사망한 플레이어가 GameRoom.ReviveDelay만큼 지나 자동 부활했을 때 방 전체에 보낸다(체력은 가득 찬 상태).
    // 부활 위치는 서버가 맵 정보를 모르므로 정하지 않는다 - 본인 클라이언트가 현재 맵의 RespawnPoint로 옮긴 뒤
    // 평소처럼 Game_MoveRequest로 알리고, 다른 클라이언트는 그 이동 브로드캐스트로 위치를 받는다.
    public class S2CPlayerRevived
    {
        public long PlayerId { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(CurrentHp);
            writer.Write(MaxHp);
        });

        public static S2CPlayerRevived Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CPlayerRevived
        {
            PlayerId = reader.ReadInt64(),
            CurrentHp = reader.ReadInt32(),
            MaxHp = reader.ReadInt32()
        });
    }
}
