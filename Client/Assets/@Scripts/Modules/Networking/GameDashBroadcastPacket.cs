namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CDashBroadcast.cs와 형식이 동일해야 한다.
    // 다른 플레이어가 대쉬를 시작했음을 알린다(본인의 대쉬는 로컬에서 즉시 재생하므로 오지 않는다).
    // RemoteCharacterController.PlayDash가 이 값으로 전방/후방 대쉬 모션과 이펙트를 재생한다.
    public class GameDashBroadcastPacket
    {
        public long PlayerId;
        public bool IsBackward;

        public static GameDashBroadcastPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameDashBroadcastPacket
        {
            PlayerId = reader.ReadInt64(),
            IsBackward = reader.ReadBoolean()
        });
    }
}
