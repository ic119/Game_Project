namespace GameServer.Combat
{
    // MainServer(AuthServer)의 GET /api/characters/{id} 응답에서 전투 스탯 계산에 필요한 부분만 뽑아낸 값.
    // PlayerAuthValidator.FetchOwnedCharacterAsync가 채워주며, CombatStatCalculator가 이 값으로
    // AttackPower/Defense를 서버 권위로 계산한다. str/agi/장착 아이템 모두 MainServer DB가 원본이라
    // 클라이언트가 위조할 수 없다.
    public record CharacterCombatSnapshot(int Str, int Agi, IReadOnlyList<string> EquippedItemIds);
}
