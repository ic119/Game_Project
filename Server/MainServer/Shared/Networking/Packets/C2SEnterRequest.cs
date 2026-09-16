namespace Shared.Networking.Packets
{
    // Client Assets/@Scripts/Modules/Networking/GameEnterRequestPacket.cs와 바이트 단위로 동일해야 한다.
    // Game_EnterRequest 전용 래퍼 - AccessToken은 본인 인증에만 쓰이므로 PlayerInfo(Game_EnterAck/
    // Game_PlayerJoined로 다른 플레이어에게도 브로드캐스트됨)에는 넣지 않고 이 패킷에만 담는다.
    public class C2SEnterRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public PlayerInfo Player { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(AccessToken);
            Player.WriteTo(writer);
        });

        public static C2SEnterRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SEnterRequest
        {
            AccessToken = reader.ReadString(),
            Player = PlayerInfo.ReadFrom(reader)
        });
    }
}
