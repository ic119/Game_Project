namespace Shared.Networking.Packets
{
    // 대쉬 시작 알림(C2S). 서버는 쿨다운을 검증한 뒤 짧은 무적 구간을 기록해 몬스터 공격 판정(GameRoom.ResolveMonsterAttack)에서
    // 회피를 인정한다. 위치는 담지 않는다 - 대쉬 이동 자체는 Game_MoveRequest 검증(이동 거리 예산)이 따로 처리한다.
    // 후방 여부는 다른 플레이어 화면에 어떤 대쉬 모션을 보여줄지(Game_DashBroadcast)에만 쓰이며 판정에는 영향이 없다.
    public class C2SDashRequest
    {
        public long PlayerId { get; set; }

        // true면 후방 대쉬. 연출용이라 위조돼도 다른 플레이어 화면에 다른 모션이 보이는 것 이상의 피해가 없다.
        public bool IsBackward { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(IsBackward);
        });

        public static C2SDashRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SDashRequest
        {
            PlayerId = reader.ReadInt64(),
            IsBackward = reader.ReadBoolean()
        });
    }
}
