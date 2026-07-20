using UnityEngine;

public class AuthResult : MonoBehaviour
{
    public bool Success;
    public string PlayFabId;
    public string ErrorMessage;

    public static AuthResult Ok(string playFabId)
    {
        return new AuthResult { Success = true, PlayFabId = playFabId };
    }
}
