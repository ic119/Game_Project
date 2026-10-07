namespace Shared.Networking.Packets
{
    // 플레이어 본인의 마나가 바뀌었을 때 본인에게만 보낸다(자연 회복, 레벨업/부활로 가득 참, 이후 스킬 사용으로 소모).
    // 체력과 달리 다른 접속자에게는 알리지 않는다 - 마나 바는 본인 화면에만 있다. MaxMp가 함께 오는 이유는 레벨업으로
    // 최대 마나가 바뀌는 경우를 같은 패킷으로 처리하기 위해서다.
    public class S2CPlayerMpUpdate
    {
        public long PlayerId { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(CurrentMp);
            writer.Write(MaxMp);
        });

        public static S2CPlayerMpUpdate Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CPlayerMpUpdate
        {
            PlayerId = reader.ReadInt64(),
            CurrentMp = reader.ReadInt32(),
            MaxMp = reader.ReadInt32()
        });
    }
}
