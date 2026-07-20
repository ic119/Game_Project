using UnityEngine;

public class PlayerSession : MonoBehaviour
{
    public static string PlayFabId { get; private set; }
    public static string SessionTicket { get; private set; }    

    public static bool IsLoggedIn => !string.IsNullOrEmpty(PlayFabId);

    public static void SetSession(string playFabId, string sessionTicket)
    {
        PlayFabId = playFabId;
        SessionTicket = sessionTicket;
    }

    public static void Clear()
    {
        PlayFabId = null;
        SessionTicket = null;
    }
}
