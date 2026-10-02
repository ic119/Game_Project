namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CActiveGate.cs와 필드 순서가 일치해야 한다.
    // Game_EnterAck/Game_MapChangeAck 직후, 상자 목록(Game_ActiveChestsNotify) 다음에 온다.
    // 방 생성 시 후보 중에서 뽑힌 던전 게이트의 위치다. 게이트가 없는 맵이면 HasGate가 false다(항상 온다).
    public class GameActiveGatePacket
    {
        public bool HasGate;

        // DungeonGateSpawnPointMarker GameObject 이름과 같은 값.
        public string Id;
        public float X;
        public float Y;
        public float Z;

        public static GameActiveGatePacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameActiveGatePacket
        {
            HasGate = reader.ReadBoolean(),
            Id = reader.ReadString(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle()
        });
    }
}
