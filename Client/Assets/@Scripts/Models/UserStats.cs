using System;

/// <summary>
/// 캐릭터 생성 시점의 기본 능력치 데이터.
/// </summary>
[Serializable]
public class UserStats
{
    public int str;
    public int agi;
    public int intel;

    public static UserStats CreateDefault()
    {
        return new UserStats
        {
            str = 5,
            agi = 5,
            intel = 5
        };
    }

    public int GetTotal()
    {
        return str + agi + intel;
    }
}
