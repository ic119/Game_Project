namespace Shared.Networking
{
    // 상위 1바이트 = 도메인, 하위 1바이트 = 액션 (0x{Domain}{Action})
    public enum OpCode : ushort
    {
        // 0x00XX = Gateway/시스템 공용
        System_Heartbeat = 0x0001,
        System_Error = 0x0002,

        // 0x01XX = Auth 관련 (참고용, 실제 인증은 HTTP)

        // 0x02XX = Game
        Game_MoveRequest = 0x0201,
        Game_MoveBroadcast = 0x0202,
        Game_EnterRequest = 0x0203,
        Game_EnterAck = 0x0204,
        Game_PlayerJoined = 0x0205,
        Game_PlayerLeft = 0x0206,

        // 0x03XX = Dungeon
    }
}
