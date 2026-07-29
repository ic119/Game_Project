using PlayFab;
using PlayFab.ClientModels;
using System;
using UnityEngine;

public class PlayFabAuthService : MonoBehaviour, IAuthService
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

    public void LoginWithAccount(string username, string password, Action<AuthResult> onComplete)
    {
        var request = new LoginWithPlayFabRequest
        {
            Username = username,
            Password = password
        };

        PlayFabClientAPI.LoginWithPlayFab(request,
            result => HandleSuccess(result, onComplete),
            error => HandleFailure(error, onComplete));
    }

    public void RegisterAccount(string username, string password, string email, Action<AuthResult> onComplete)
    {
        var request = new RegisterPlayFabUserRequest
        {
            Username = username,
            Password = password,
            Email = email,
            RequireBothUsernameAndEmail = true
        };

        PlayFabClientAPI.RegisterPlayFabUser(request,
            result => HandleSuccess(result.PlayFabId, result.SessionTicket, onComplete),
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
        HandleSuccess(result.PlayFabId, result.SessionTicket, onComplete);
    }

    private void HandleSuccess(string playFabId, string sessionTicket, Action<AuthResult> onComplete)
    {
        PlayerSession.SetSession(playFabId, sessionTicket);
        onComplete(AuthResult.Ok(playFabId));
    }

    private void HandleFailure(PlayFabError error, Action<AuthResult> onComplete)
    {
        Debug.LogWarning($"[PlayFabAuthService] {error.GenerateErrorReport()}");
        onComplete(AuthResult.Fail(error.ErrorMessage));
    }
}
