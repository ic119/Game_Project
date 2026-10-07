namespace Shared
{
    // 무기 종류. 값은 클라이언트 Define.cs의 WeaponType과 같아야 한다(공격 모션 패킷이 이 숫자를 그대로 싣는다).
    // 서버는 ItemDefinitions.json의 "weaponType"(이름 문자열)으로 장착 무기의 종류를 정한다 - 클라이언트가 보낸 값을 믿지 않는다.
    public enum WeaponKind
    {
        None = 0,
        OneHanded = 1,
        TwoHanded = 2,
        Wand = 4,
        Spear = 5
    }
}
