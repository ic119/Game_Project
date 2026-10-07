namespace Shared.Networking.Packets
{
    // 대쉬 모션 전용 알림(S2C). Game_DashRequest가 쿨다운 검증을 통과했을 때만 playerId를 보고 있는 사람에게 중계된다
    // (본인은 이미 로컬에서 즉시 재생했으므로 받지 않는다). 위치 이동은 Game_WorldSnapshot 보간이 맡고, 이 알림은 원격 캐릭터의
    // 대쉬 모션/이펙트 재생에만 쓰인다.
    public class S2CDashBroadcast
    {
        public long PlayerId { get; set; }

        // true면 후방 대쉬(BackDash 모션), false면 전방 대쉬(Dash 모션).
        public bool IsBackward { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(IsBackward);
        });

        public static S2CDashBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CDashBroadcast
        {
            PlayerId = reader.ReadInt64(),
            IsBackward = reader.ReadBoolean()
        });
    }
}
