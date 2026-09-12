namespace Incheol.Modules.Networking
{
    // 서버(Shared/Networking/OpCode.cs)와 값이 반드시 일치해야 한다.
    public enum GameOpCode : ushort
    {
        System_Heartbeat = 0x0001,
        System_Error = 0x0002,

        Game_MoveRequest = 0x0201,
        Game_MoveBroadcast = 0x0202,
        Game_EnterRequest = 0x0203,
        Game_EnterAck = 0x0204,
        Game_PlayerJoined = 0x0205,
        Game_PlayerLeft = 0x0206
    }
}
