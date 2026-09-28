namespace Shared.Networking.Packets
{
    // 서버가 이동 요청(Game_MoveRequest)을 거부했을 때 본인에게만 보낸다 - 허용 속도를 넘는 이동(스피드핵/순간이동)이거나
    // 네트워크 지연으로 이동이 몰려 들어온 경우다. 서버가 마지막으로 인정한 위치를 담아 보내며, 클라이언트는
    // 그 위치로 되돌아가야 한다(다른 접속자는 거부된 이동을 애초에 받지 않았다).
    public class S2CPositionCorrection
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        });

        public static S2CPositionCorrection Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CPositionCorrection
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle()
        });
    }
}
