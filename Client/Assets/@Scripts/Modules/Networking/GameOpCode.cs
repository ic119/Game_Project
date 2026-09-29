namespace Incheol.Modules.Networking
{
    // 서버(Shared/Networking/OpCode.cs)와 값이 반드시 일치해야 한다.
    public enum GameOpCode : ushort
    {
        System_Heartbeat = 0x0001,
        System_Error = 0x0002,
        System_Kicked = 0x0003,

        Game_MoveRequest = 0x0201,
        // 0x0202(Game_MoveBroadcast)는 Game_WorldSnapshot으로 대체되어 사용하지 않는다(번호 재사용 금지).
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
        Game_ExpGainBroadcast = 0x0211,
        // 0x0212(Game_MonsterMoveBroadcast)는 Game_WorldSnapshot으로 대체되어 사용하지 않는다(번호 재사용 금지).
        Game_MonsterAttackBroadcast = 0x0213,
        Game_LootBroadcast = 0x0214,
        Game_StatUpdateRequest = 0x0215,
        Game_PlayerHpBroadcast = 0x0216,
        Game_PlayerRevived = 0x0217,
        Game_UseItemRequest = 0x0218,
        Game_UseItemResult = 0x0219,
        Game_PositionCorrection = 0x021A,
        Game_WorldSnapshot = 0x021B,
        Game_MonsterLeaveView = 0x021C,
        Game_AttackAnimationRequest = 0x021D,
        Game_AttackAnimationBroadcast = 0x021E,
        Game_ChestOpenRequest = 0x021F,
        Game_ChestOpenBroadcast = 0x0220
    }
}
