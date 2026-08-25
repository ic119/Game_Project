using Incheol.Utils;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Incheol.Modules
{
    public class ServerConnectManager : SingletonObject<ServerConnectManager>
    {
        #region Variable
        [Header("Auth 서버 접속 설정")]
        [Tooltip("MainServer(AuthServer) 기본 URL. HTTPS 사용 시 로컬 개발 인증서를 신뢰해야 한다(dotnet dev-certs https --trust).")]
        [SerializeField] private string serverBaseUrl = "https://localhost:58208";
        [SerializeField] private float requestTimeoutSeconds = 10f;

        public bool IsLoggedIn { get; private set; }
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public UserInfo CurrentUser { get; private set; }

        /// <summary>
        /// 로그인 세션(AccessToken/RefreshToken)은 씬이 전환되어도 유지되어야 하므로 파괴되지 않는다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;
        #endregion

        #region DTO
        [Serializable]
        public class UserInfo
        {
            public long _id;
            public string _username;
            public string _nickname;
            public string _createdAt;
        }

        [Serializable] private class RegisterRequestBody { public string _userName; public string _password; public string _nickname; }
        [Serializable] private class LoginRequestBody { public string _username; public string _password; }
        [Serializable] private class RefreshRequestBody { public string _refreshToken; }
        [Serializable] private class LoginResponseBody { public string _accessToken; public string _refreshToken; public UserInfo _user; }
        [Serializable] private class ErrorResponseBody { public string message; }
        #endregion

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 로컬 개발 서버(dotnet dev-certs https --trust)의 자체 서명 인증서를 신뢰하기 위한 우회 핸들러.
        /// dotnet dev-certs가 신뢰시키는 곳은 OS 인증서 저장소뿐이라 UnityTls(UnityWebRequest의 검증 로직)는
        /// 이 인증서를 여전히 모르는 CA로 취급해 SSL 핸드셰이크에서 실패한다(Curl error 60 / UnityTls error 7).
        /// 실제 서버 인증서 검증을 완전히 생략하므로 UNITY_EDITOR/DEVELOPMENT_BUILD로 제한해 프로덕션 배포 빌드에는
        /// 절대 포함되지 않게 한다.
        /// </summary>
        private class LocalDevCertificateHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] _certificateData)
            {
                return true;
            }
        }
#endif

        #region Method - Auth API
        /// <summary>
        /// 회원가입을 요청한다(POST /api/users/register). _onComplete는 (성공 여부, 실패 시 서버 메시지)로 호출된다.
        /// </summary>
        public void Register(string _userName, string _password, string _nickname, Action<bool, string> _onComplete = null)
        {
            _ = RegisterAsync(_userName, _password, _nickname, _onComplete);
        }

        /// <summary>
        /// 로그인을 요청한다(POST /api/auth/login). 성공하면 AccessToken/RefreshToken/CurrentUser가 갱신된다.
        /// </summary>
        public void Login(string _username, string _password, Action<bool, string> _onComplete = null)
        {
            _ = LoginAsync(_username, _password, _onComplete);
        }

        /// <summary>
        /// 저장된 RefreshToken으로 토큰을 재발급받는다(POST /api/auth/refresh, 토큰 로테이션 — RefreshToken도 함께 갱신됨).
        /// </summary>
        public void Refresh(Action<bool, string> _onComplete = null)
        {
            _ = RefreshAsync(_onComplete);
        }

        /// <summary>
        /// 서버에 로그아웃(RefreshToken revoke, POST /api/auth/logout)을 요청하고, 결과와 무관하게 로컬 세션은 정리한다.
        /// </summary>
        public void Logout(Action<bool> _onComplete = null)
        {
            _ = LogoutAsync(_onComplete);
        }
        #endregion

        #region Method - Internal
        private async Awaitable RegisterAsync(string _userName, string _password, string _nickname, Action<bool, string> _onComplete)
        {
            string json = JsonUtility.ToJson(new RegisterRequestBody { _userName = _userName, _password = _password, _nickname = _nickname });
            (bool success, string _, string error) = await SendJsonRequestAsync("/api/users/register", "POST", json);

            _onComplete?.Invoke(success, error);
        }

        private async Awaitable LoginAsync(string _username, string _password, Action<bool, string> _onComplete)
        {
            string json = JsonUtility.ToJson(new LoginRequestBody { _username = _username, _password = _password });
            (bool success, string body, string error) = await SendJsonRequestAsync("/api/auth/login", "POST", json);

            if (!success)
            {
                _onComplete?.Invoke(false, error);
                return;
            }

            ApplyLoginResponse(body);
            _onComplete?.Invoke(true, null);
        }

        private async Awaitable RefreshAsync(Action<bool, string> _onComplete)
        {
            if (string.IsNullOrEmpty(RefreshToken))
            {
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>("저장된 RefreshToken이 없어 재발급을 요청할 수 없습니다.");
                _onComplete?.Invoke(false, "로그인이 필요합니다.");
                return;
            }

            string json = JsonUtility.ToJson(new RefreshRequestBody { _refreshToken = RefreshToken });
            (bool success, string body, string error) = await SendJsonRequestAsync("/api/auth/refresh", "POST", json);

            if (!success)
            {
                ClearSession();
                _onComplete?.Invoke(false, error);
                return;
            }

            ApplyLoginResponse(body);
            _onComplete?.Invoke(true, null);
        }

        private async Awaitable LogoutAsync(Action<bool> _onComplete)
        {
            if (string.IsNullOrEmpty(RefreshToken))
            {
                ClearSession();
                _onComplete?.Invoke(true);
                return;
            }

            string json = JsonUtility.ToJson(new RefreshRequestBody { _refreshToken = RefreshToken });
            (bool success, string _, string error) = await SendJsonRequestAsync("/api/auth/logout", "POST", json);

            ClearSession();

            if (!success)
            {
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>($"로그아웃 요청이 실패했지만 로컬 세션은 정리했습니다 : {error}");
            }

            _onComplete?.Invoke(success);
        }

        private void ApplyLoginResponse(string _json)
        {
            LoginResponseBody response = JsonUtility.FromJson<LoginResponseBody>(_json);
            if (response == null || string.IsNullOrEmpty(response._accessToken))
            {
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>("로그인 응답 파싱에 실패했습니다.");
                return;
            }

            AccessToken = response._accessToken;
            RefreshToken = response._refreshToken;
            CurrentUser = response._user;
            IsLoggedIn = true;
        }

        private void ClearSession()
        {
            AccessToken = null;
            RefreshToken = null;
            CurrentUser = null;
            IsLoggedIn = false;
        }

        /// <summary>
        /// JSON Body로 서버에 요청을 보내고 완료될 때까지 매 프레임 대기한다.
        /// HTTP 상태 코드가 에러(4xx/5xx)이거나 네트워크 오류인 경우 success=false와 함께
        /// 서버가 { message: "..." } 형식으로 내려준 에러 메시지를 파싱해 반환한다.
        /// </summary>
        private async Awaitable<(bool success, string body, string error)> SendJsonRequestAsync(string _path, string _method, string _jsonBody)
        {
            string url = serverBaseUrl.TrimEnd('/') + _path;

            using UnityWebRequest request = new UnityWebRequest(url, _method);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(_jsonBody ?? string.Empty));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 로컬 개발 서버의 자체 서명 인증서는 UnityTls의 루트 CA 목록에 없어 기본 검증으로는 항상 실패한다.
            // 프로덕션 빌드에는 이 우회가 포함되지 않는다(위 LocalDevCertificateHandler 선언부 참고).
            request.certificateHandler = new LocalDevCertificateHandler();
#endif

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Awaitable.NextFrameAsync();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMessage = ExtractErrorMessage(request);
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>($"요청 실패 [{_method} {_path}] : {errorMessage}");
                return (false, request.downloadHandler.text, errorMessage);
            }

            return (true, request.downloadHandler.text, null);
        }

        private static string ExtractErrorMessage(UnityWebRequest _request)
        {
            string body = _request.downloadHandler?.text;

            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    ErrorResponseBody error = JsonUtility.FromJson<ErrorResponseBody>(body);
                    if (error != null && !string.IsNullOrEmpty(error.message))
                    {
                        return error.message;
                    }
                }
                catch (Exception)
                {
                    // 에러 바디가 JSON 형식이 아닌 경우(예: 연결 자체 실패)에는 아래 request.error로 대체한다.
                }
            }

            return _request.error;
        }
        #endregion
    }
}
