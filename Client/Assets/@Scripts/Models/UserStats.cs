using System;

// 캐릭터 생성 시점의 기본 능력치 데이터.
[Serializable]
public class UserStats : IStatBlock
{
    public int str;
    public int agi;
    public int intel;

    public int GetTotal() => str + agi + intel;

    // 캐릭터 생성 시 사용하는 기본 능력치. UI 표시용 값과 실제 저장값이 갈라지지 않도록
    // 이 메서드 하나를 유일한 기준(source of truth)으로 사용한다.
    public static UserStats CreateDefault()
    {
        return new UserStats
        {
            str = 10,
            agi = 10,
            intel = 10
        };
    }
}
