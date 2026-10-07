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

        // 명중 여부/대상 유무와 무관하게 콤보 타수마다 보내는 공격 "모션" 전용 알림. 대상이 있어야만 보내는
        // Game_AttackRequest/Game_MonsterAttackRequest와 달리 데미지/쿨다운 판정에는 쓰이지 않고 그대로 중계만 된다.
        Game_AttackAnimationRequest = 0x021D,
        Game_AttackAnimationBroadcast = 0x021E,

        // 보물상자 개봉. 선착순(먼저 연 사람이 임자)이라 GameRoom이 상자별로 한 번만 성공시킨다.
        // 실제 골드/아이템 지급은 본인에게만(Game_LootBroadcast 재사용), 뚜껑이 열렸다는 사실은 방 전체에 알린다.
        Game_ChestOpenRequest = 0x021F,
        Game_ChestOpenBroadcast = 0x0220,

        // 방에 입장/맵 이동한 플레이어에게 "이 방에 지금 서 있는 상자 목록"(고정 상자 + 방 생성 시 후보에서 뽑힌 상자)을
        // 알린다. Game_EnterAck/Game_MapChangeAck 직후, 이미 열린 상자를 따라잡는 Game_ChestOpenBroadcast보다 먼저 온다.
        Game_ActiveChestsNotify = 0x0221,

        // 상자 리스폰. 열린 상자가 잠시 뒤 사라지고(Despawn), 같은 등급의 다른 후보 지점에 새 상자가 생긴다(Spawn).
        // 둘 다 방 전체에 보낸다(Game_ChestOpenBroadcast와 같은 방식). 방에 새로 들어온 사람은 Game_ActiveChestsNotify로 따라잡는다.
        Game_ChestSpawnBroadcast = 0x0222,
        Game_ChestDespawnBroadcast = 0x0223,

        // 다른 플레이어의 장착 장비(무기/갑옷/투구)가 바뀌었음을 그 플레이어를 보고 있는 사람에게 알린다(외형 동기화).
        Game_EquipmentChangedBroadcast = 0x0224,

        // 대쉬 회피. 클라이언트가 대쉬를 시작하면 Game_DashRequest로 알리고, 서버가 쿨다운을 검증한 뒤 짧은 무적 구간을 기록한다.
        // 몬스터 공격은 Game_MonsterAttackStartBroadcast(선딜 시작, 공격 모션 재생)와 선딜이 끝난 뒤의 판정으로 나뉜다 -
        // 판정 결과가 명중이면 기존 Game_MonsterAttackBroadcast(피해/남은 체력), 회피면 Game_MonsterAttackDodgedBroadcast다.
        Game_DashRequest = 0x0225,
        Game_MonsterAttackStartBroadcast = 0x0226,
        Game_MonsterAttackDodgedBroadcast = 0x0227,

        // 맵 이동 거부. 클라이언트는 Game_MapChangeRequest를 보낸 뒤 서버의 응답(승인: Game_MapChangeAck, 거부: 이것)을 받고서야
        // 맵을 교체한다 - 거부된 요청이 이미 새 맵으로 넘어간 클라이언트와 서버의 맵을 어긋나게 만들지 않도록 한다.
        Game_MapChangeRejected = 0x0228,

        // 방에 입장/맵 이동한 플레이어에게 "이 방의 던전 게이트가 어느 후보 지점에 섰는지"를 알린다(방 생성 시 후보 중 한 곳이 뽑힌다).
        // Game_ActiveChestsNotify 직후에 온다. 게이트가 없는 맵이어도 "없음"으로 항상 온다.
        Game_ActiveGateNotify = 0x0229,

        // 보스 스킬. 시전을 시작하면(예고) Game_BossSkillTelegraphBroadcast로 위험 범위와 시간을 알리고, 예고가 끝나 발동하거나
        // 취소되면 Game_BossSkillEndBroadcast를 보낸다. 판정 결과(명중/회피)는 기존 Game_MonsterAttackBroadcast/
        // Game_MonsterAttackDodgedBroadcast를 그대로 쓴다.
        Game_BossSkillTelegraphBroadcast = 0x022A,
        Game_BossSkillEndBroadcast = 0x022B,

        // 대쉬 모션 중계. Game_DashRequest가 쿨다운 검증을 통과하면 그 플레이어를 보고 있는 사람에게 알린다(본인은 이미 로컬에서
        // 재생했으므로 받지 않는다). 위치 이동은 Game_WorldSnapshot 보간이 하므로 이 알림은 모션/이펙트 재생에만 쓰인다.
        Game_DashBroadcast = 0x022C,

        // 플레이어 본인의 마나가 바뀌었음을 본인에게만 알린다(자연 회복, 레벨업/부활 충전, 이후 스킬 사용 소모). 체력(Game_PlayerHpBroadcast)과 달리
        // 다른 플레이어의 마나는 화면에 그리지 않으므로 주변에 보내지 않는다.
        Game_PlayerMpUpdate = 0x022D,

        // 액티브 스킬(숫자 키 1~4). 클라이언트가 슬롯과 방향으로 요청하면(Game_SkillRequest) 서버가 해금 레벨/쿨다운/마나를 검증해
        // 결과를 본인에게 알리고(Game_SkillResult), 승인된 시전은 주변 플레이어에게 모션/이펙트용으로 중계한다(Game_SkillCastBroadcast).
        // 피해는 타격 시점에 기존 Game_MonsterDamageBroadcast로 나간다.
        Game_SkillRequest = 0x022E,
        Game_SkillResult = 0x022F,
        Game_SkillCastBroadcast = 0x0230,

        // 0x03XX = Dungeon
    }
}
