namespace Shared.Networking.Packets
{
    // 대쉬 시작 알림(C2S). 서버는 쿨다운을 검증한 뒤 짧은 무적 구간을 기록해 몬스터 공격 판정(GameRoom.ResolveMonsterAttack)에서
    // 회피를 인정한다. 위치/방향은 담지 않는다 - 대쉬 이동 자체는 Game_MoveRequest 검증(이동 거리 예산)이 따로 처리한다.
    public class C2SDashRequest
    {
        public long PlayerId { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
        });

        public static C2SDashRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SDashRequest
        {
            PlayerId = reader.ReadInt64()
        });
    }
}
