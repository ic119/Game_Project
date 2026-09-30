namespace GameServer.Combat
{
    // 다른 접속자에게 보여줄 외형 장비(무기/갑옷/투구)의 itemId. CharacterSnapshot(DB 원본)에서 뽑아 서버가 정한다 -
    // 클라이언트가 보낸 값을 그대로 브로드캐스트하면 가지지 않은 장비를 입은 것처럼 보이게 만들 수 있기 때문이다.
    // 서버 아이템 정의에 없는 itemId(삭제된 아이템 등)는 빈 문자열로 취급한다 - 받는 클라이언트도 모르는 아이템이라 그릴 수 없다.
    public readonly record struct EquippedVisuals(string WeaponItemId, string ArmorItemId, string HelmetItemId)
    {
        public const string WeaponSlot = "Weapon";
        public const string ArmorSlot = "Armor";
        public const string HelmetSlot = "Helmet";

        public static EquippedVisuals From(CharacterSnapshot snapshot, Func<string, bool> itemExists)
        {
            string Pick(string slot)
            {
                return snapshot.EquippedBySlot is not null
                    && snapshot.EquippedBySlot.TryGetValue(slot, out string? itemId)
                    && !string.IsNullOrEmpty(itemId)
                    && itemExists(itemId)
                        ? itemId
                        : string.Empty;
            }

            return new EquippedVisuals(Pick(WeaponSlot), Pick(ArmorSlot), Pick(HelmetSlot));
        }
    }
}
