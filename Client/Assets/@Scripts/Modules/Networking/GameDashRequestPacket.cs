namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SDashRequest.cs와 형식이 동일해야 한다.
    // 대쉬를 시작할 때마다 보내는 알림. 서버가 쿨다운을 검증한 뒤 짧은 무적 구간을 기록해 몬스터 공격을 회피로 판정한다.
    public class GameDashRequestPacket
    {
        public long PlayerId;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
        });
    }
}
