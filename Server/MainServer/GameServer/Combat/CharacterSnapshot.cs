using System.Text.Json;

namespace GameServer.Combat
{
    // MainServer(AuthServer)의 캐릭터 조회 응답(GET /api/characters/{id} 또는 서버 간 GET /api/internal/characters/{id})에서
    // GameServer가 쓰는 부분만 뽑아낸 값. ClientSession이 Game_EnterRequest 때 PlayerInfo의 닉네임/외형/레벨/경험치를
    // 이 값으로 덮어쓰고, CombatStatCalculator가 전투 스탯(공격력/방어력/최대 체력)을 서버 권위로 계산한다.
    // 모두 MainServer DB가 원본이라 클라이언트가 위조할 수 없다.
    public record CharacterSnapshot(
        string Nickname,
        int HairIndex,
        int EyeIndex,
        int MouthIndex,
        int Level,
        int Exp,
        int Str,
        int Agi,
        IReadOnlyList<string> EquippedItemIds,
        IReadOnlyDictionary<string, string>? EquippedBySlot = null,
        // 지능(최대 마나 계산에 쓴다). 기존 호출부(테스트 포함)를 깨지 않도록 맨 뒤 선택 인자로 둔다. 기본값은 MainServer가 캐릭터를
        // 만들 때 주는 값(CharacterService.DefaultStat)과 같고, 아래 DefaultIntel과 같은 값이어야 한다(기본 매개변수에는 이 레코드의
        // 상수를 쓸 수 없어 리터럴로 적었다 - CombatStatCalculatorTests가 둘이 같은지 검사한다).
        int Intel = 10)
    {
        public const int DefaultIntel = 10;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // MainServer CharacterResponse JSON을 파싱한다(두 조회 경로가 같은 응답 형식을 쓴다). 파싱할 수 없으면 null.
        public static CharacterSnapshot? FromCharacterResponseJson(string json)
        {
            var body = JsonSerializer.Deserialize<CharacterResponseBody>(json, JsonOptions);
            if (body is null)
            {
                return null;
            }

            List<string> equippedItemIds = (body._items ?? new List<CharacterItemResponseBody>())
                .Where(item => !string.IsNullOrEmpty(item._equipSlot))
                .Select(item => item._itemId)
                .ToList();

            // 슬롯 -> itemId. 외형 동기화(EquippedVisuals)가 어느 슬롯에 무엇이 장착됐는지 알아야 한다.
            var equippedBySlot = new Dictionary<string, string>();
            foreach (CharacterItemResponseBody item in body._items ?? new List<CharacterItemResponseBody>())
            {
                if (!string.IsNullOrEmpty(item._equipSlot))
                {
                    equippedBySlot[item._equipSlot] = item._itemId;
                }
            }

            return new CharacterSnapshot(
                body._nickname,
                body._hairIndex,
                body._eyeIndex,
                body._mouthIndex,
                body._level,
                body._exp,
                body._str,
                body._agi,
                equippedItemIds,
                equippedBySlot,
                body._intel);
        }

        // MainServer.CharacterServer.DTOs.CharacterResponse의 부분 집합. GameServer는 MainServer 프로젝트를
        // 참조하지 않으므로(별도 배포 단위) GameServer가 쓰는 필드만 별도로 선언해 파싱한다.
        // MainServer 쪽 DTO 필드명이 바뀌면 이 클래스도 함께 맞춰야 한다.
        private class CharacterResponseBody
        {
            public string _nickname { get; set; } = string.Empty;
            public int _hairIndex { get; set; }
            public int _eyeIndex { get; set; }
            public int _mouthIndex { get; set; }
            public int _level { get; set; }
            public int _exp { get; set; }
            public int _str { get; set; }
            public int _agi { get; set; }
            // 응답에 _intel이 없으면(구버전 MainServer) 기본 지능으로 계산한다 - 0이 되어 최대 마나가 비정상적으로 작아지지 않게 한다.
            public int _intel { get; set; } = DefaultIntel;
            public List<CharacterItemResponseBody>? _items { get; set; }
        }

        private class CharacterItemResponseBody
        {
            public string _itemId { get; set; } = string.Empty;
            public string? _equipSlot { get; set; }
        }
    }
}
