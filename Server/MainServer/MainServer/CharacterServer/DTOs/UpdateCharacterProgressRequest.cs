namespace MainServer.CharacterServer.DTOs
{
    // GameServer(TCP)가 몬스터 처치로 계산한 레벨/경험치를 클라이언트가 전달받아 저장을 요청할 때 쓴다.
    // GameServer 자체는 DB 접근 권한이 없어 직접 저장할 수 없으므로, 클라이언트가 이 값을 그대로 실어 보낸다.
    public record UpdateCharacterProgressRequest(int _level, int _exp);
}
