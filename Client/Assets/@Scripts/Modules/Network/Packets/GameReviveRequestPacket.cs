namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SReviveRequest.cs와 형식이 동일해야 한다.
    // 사망 후 부활 선택(마을/제자리). 부활 위치는 담지 않는다 - 서버가 정해 Game_PlayerRevived로 알려 준다.
    public class GameReviveRequestPacket
    {
        public long PlayerId;

        // true면 제자리 부활, false면 마을 부활.
        public bool InPlace;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(InPlace);
        });
    }
}
