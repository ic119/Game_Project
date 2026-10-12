namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/C2SMapChangeRequest.cs와 형식이 동일해야 한다.
    public class GameMapChangeRequestPacket
    {
        public long PlayerId;
        public string MapId = string.Empty;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        public byte[] Encode() => GameBinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(MapId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        });
    }
}
