namespace MainServer.CharacterServer.DTOs
{
    public record CharacterResponse(
        long _id,
        string _nickname,
        int _hairIndex,
        int _eyeIndex,
        int _mouthIndex,
        int _str,
        int _agi,
        int _intel,
        int _level,
        int _exp,
        DateTime? _lastLoginAt,
        DateTime _createdAt);
}
