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
        Game_PlayerLeft = 0x0206,
        Game_ChatRequest = 0x0207,
        Game_ChatBroadcast = 0x0208,
        Game_AttackRequest = 0x0209,
        Game_DamageBroadcast = 0x020A,
        Game_MapChangeRequest = 0x020B,
        Game_MapChangeAck = 0x020C,
        Game_MonsterAttackRequest = 0x020D,
        Game_MonsterDamageBroadcast = 0x020E,
        Game_MonsterDieBroadcast = 0x020F,
        Game_MonsterSpawnBroadcast = 0x0210,
        Game_ExpGainBroadcast = 0x0211
    }
}
