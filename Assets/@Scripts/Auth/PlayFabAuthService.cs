using PlayFab;
using PlayFab.ClientModels;
using System;
using UnityEngine;

public class PlayFabAuthService : MonoBehaviour
{
    public void LoginWithDeviceId(Action<AuthResult> onComplete)
    {
        var request = new LoginWithCustomIDRequest
        {
            CustomId = SystemInfo.deviceUniqueIdentifier,
            CreateAccount = true
        };

        PlayFabClientAPI.LoginWithCustomID(request,
            result => HandleSuccess(result, onComplete),
            error => HandleFailure(error, onComplete));
    }

    public void LoginWithGoogle(string idToken, Action<AuthResult> onComplete)
    {
        var request = new LoginWithGoogleAccountRequest
        {
            ServerAuthCode = idToken,
            CreateAccount = true
        };

        PlayFabClientAPI.LoginWithGoogleAccount(request,
            result => HandleSuccess(result, onComplete),
            error => HandleFailure(error, onComplete));
    }

    public void LinkGoogle(string idToken, Action<AuthResult> onComplete)
    {
        var request = new LinkGoogleAccountRequest
        {
            ServerAuthCode = idToken
        };

        PlayFabClientAPI.LinkGoogleAccount(request,
            _ => onComplete(AuthResult.Ok(PlayerSession.PlayFabId)),
            error => HandleFailure(error, onComplete));
    }

    public void Logout()
    {
        PlayFabClientAPI.ForgetAllCredentials();
        PlayerSession.Clear();
    }

    private void HandleSuccess(LoginResult result, Action<AuthResult> onComplete)
    {
        PlayerSession.SetSession(result.PlayFabId, result.SessionTicket);
        onComplete(AuthResult.Ok(result.PlayFabId));
    }

    private void HandleFailure(PlayFabError error, Action<AuthResult> onComplete)
    {
        Debug.LogWarning($"[PlayFabAuthService] {error.GenerateErrorReport()}");
    }
}
