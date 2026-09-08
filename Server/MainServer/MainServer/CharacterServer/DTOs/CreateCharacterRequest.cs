namespace MainServer.CharacterServer.DTOs
{
    public record CreateCharacterRequest(string _nickname, int _hairIndex, int _eyeIndex, int _mouthIndex);
}
