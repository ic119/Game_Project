namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CPositionCorrection.cs와 형식이 동일해야 한다.
    // 서버가 내 이동 요청을 거부했을 때(허용 속도 초과) 온다. 서버가 마지막으로 인정한 위치이므로 그 자리로 되돌아간다.
    public class GamePositionCorrectionPacket
    {
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        public static GamePositionCorrectionPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GamePositionCorrectionPacket
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            RotationY = reader.ReadSingle()
        });
    }
}
