namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SEnterRequest.cs와 바이트 단위로 동일해야 한다.
    // Game_EnterRequest 전용 래퍼 - AccessToken은 본인 인증에만 쓰이므로 GamePlayerInfo(Game_EnterAck/
    // Game_PlayerJoined로 다른 플레이어에게도 브로드캐스트됨)에는 넣지 않고 이 패킷에만 담는다.
    public class GameEnterRequestPacket
    {
        public string AccessToken = string.Empty;
        public GamePlayerInfo Player = new();

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(AccessToken);
            Player.WriteTo(writer);
        });
    }
}
