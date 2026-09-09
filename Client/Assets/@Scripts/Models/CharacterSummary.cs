using System;

/// <summary>
/// 로비 캐릭터 목록 표시용 요약 데이터. 서버 CharacterResponseBody에서 목록 표시에 필요한 필드만 추려낸다.
/// </summary>
[Serializable]
public class CharacterSummary
{
    public long id;
    public string nickname;
    public int level;

    /// <summary>
    /// 마지막 접속 시간. 서버가 null/빈 값을 내려줄 수 있으며, 이 경우 UI는 "-"로 표기해야 한다.
    /// </summary>
    public string lastLoginAt;
}
