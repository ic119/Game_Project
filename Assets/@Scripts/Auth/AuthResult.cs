public class AuthResult
{
    public bool Success;
    public string PlayFabId;
    public string ErrorMessage;

    public static AuthResult Ok(string playFabId)
    {
        return new AuthResult { Success = true, PlayFabId = playFabId };
    }

    public static AuthResult Fail(string errorMessage)
    {
        return new AuthResult { Success = false, ErrorMessage = errorMessage };
    }
}
