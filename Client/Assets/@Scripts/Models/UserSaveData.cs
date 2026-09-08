using System;

/// <summary>
/// 캐릭터 생성 팝업에서 입력/선택한 값을 담아 상위 Controller에 전달하는 저장용 데이터.
/// </summary>
[Serializable]
public class UserSaveData
{
    public string nickname;
    public int hairIndex;
    public int eyeIndex;
    public int mouthIndex;

    public static UserSaveData CreateDefault(string nickname, int hairIndex, int eyeIndex, int mouthIndex)
    {
        return new UserSaveData
        {
            nickname = nickname,
            hairIndex = hairIndex,
            eyeIndex = eyeIndex,
            mouthIndex = mouthIndex
        };
    }
}
