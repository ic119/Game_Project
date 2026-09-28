namespace Shared.Networking
{
    // 상위 1바이트 = 도메인, 하위 1바이트 = 액션 (0x{Domain}{Action})
    public enum OpCode : ushort
    {
        // 0x00XX = Gateway/시스템 공용
        System_Heartbeat = 0x0001,
        System_Error = 0x0002,

        // 서버가 이 연결을 강제로 끊기 직전에 보낸다(현재는 같은 캐릭터 중복 접속). 바디는 UTF-8 사유 문자열.
        // 일반 오류(System_Error)와 구분해, 클라이언트가 "연결 끊김" 대신 전용 안내(로그인 화면 복귀 등)를 하게 한다.
        System_Kicked = 0x0003,

        // 0x01XX = Auth 관련 (참고용, 실제 인증은 HTTP)

        // 0x02XX = Game
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

        // 0x03XX = Dungeon
    }
}
