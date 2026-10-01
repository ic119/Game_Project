namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMapChangeRejected.cs의 MapChangeRejectReason과 값이 같아야 한다.
    public enum MapChangeRejectReason
    {
        None = 0,
        Dead = 1,
        NotAtPortal = 2,
        UnknownMap = 3
    }

    // 서버 Shared/Networking/Packets/S2CMapChangeRejected.cs와 형식이 동일해야 한다.
    // 맵 이동 요청(Game_MapChangeRequest)을 서버가 거부했다. 거부해도 서버 상태는 그대로이므로 클라이언트는 이전 맵을 그대로 유지한다.
    public class GameMapChangeRejectedPacket
    {
        // 클라이언트가 요청한 맵 id. 응답이 어느 요청에 대한 것인지 맞춰 보는 데 쓴다.
        public string MapId;
        public MapChangeRejectReason Reason;

        public static GameMapChangeRejectedPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameMapChangeRejectedPacket
        {
            MapId = reader.ReadString(),
            Reason = (MapChangeRejectReason)reader.ReadInt32()
        });
    }
}
