namespace GameServer.Combat
{
    // MainServer(AuthServer)의 GET /api/characters/{id} 응답에서 GameServer가 쓰는 부분만 뽑아낸 값.
    // PlayerAuthValidator.FetchOwnedCharacterAsync가 채워주며, ClientSession이 Game_EnterRequest 때 PlayerInfo의
    // 닉네임/외형/레벨/경험치를 이 값으로 덮어쓰고, CombatStatCalculator가 전투 스탯(공격력/방어력/최대 체력)을
    // 서버 권위로 계산한다. 모두 MainServer DB가 원본이라 클라이언트가 위조할 수 없다.
    public record CharacterSnapshot(
        string Nickname,
        int HairIndex,
        int EyeIndex,
        int MouthIndex,
        int Level,
        int Exp,
        int Str,
        int Agi,
        IReadOnlyList<string> EquippedItemIds);
}
