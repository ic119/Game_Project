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
        Game_MonsterMoveBroadcast = 0x0212,
        Game_MonsterAttackBroadcast = 0x0213,
        Game_LootBroadcast = 0x0214,
        Game_StatUpdateRequest = 0x0215,

        // 0x03XX = Dungeon
    }
}
