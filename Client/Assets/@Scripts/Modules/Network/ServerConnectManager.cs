 using Incheol.Utils;
using System;
using System.Text;
using System.Threading.Tasks;
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
        /// RefreshToken까지 만료/폐기되어 재발급에 실패했을 때 발생한다. 로컬 세션은 이미 정리된 상태이므로,
        /// 구독측(GameManager)은 다시 로그인하도록 로그인 화면으로 보내면 된다.
        /// </summary>
        public event Action OnSessionExpired;

        // AccessToken(서버 발급 30분)이 이 시간 안에 만료되면 요청 전에 미리 재발급한다 - 요청이 서버에 닿는 사이 만료되는 경우를 줄인다.
        private const int AccessTokenRefreshMarginSeconds = 60;

        // 진행 중인 재발급. 여러 요청이 동시에 만료를 발견해도 재발급 요청은 하나만 보낸다 - RefreshToken은 한 번 쓰면 폐기되는
        // 일회용(로테이션)이라, 두 번 보내면 늦게 도착한 쪽이 폐기된 토큰으로 실패해 멀쩡한 세션이 끊긴다.
        private Task<bool> refreshInFlight;

        // 미리 재발급(만료 임박)을 이미 시도한 AccessToken. 기기 시계가 서버보다 빠르면 새로 받은 토큰도 곧 만료로 보이므로,
        // 같은 토큰으로는 한 번만 미리 재발급한다(실제로 만료됐다면 서버의 401을 보고 다시 재발급한다).
        private string proactiveRefreshAttemptedToken;

        /// <summary>
        /// 로그인 세션(AccessToken/RefreshToken)은 씬이 전환되어도 유지되어야 하므로 파괴되지 않는다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const string MasterAccountUsername = "admin";
        private const string MasterAccountPassword = "admin1234";
        private const string MasterDevRefreshToken = "MASTER_DEV_REFRESH_TOKEN";
#endif
        #endregion

        #region LifeCycle
        protected override void Awake()
        {
            base.Awake();

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // 릴리즈 빌드인데도 인스펙터 serverBaseUrl 값이 기본값(localhost)이면 실서버 주소로 바꾸는 걸
            // 잊었을 가능성이 크다 - 조용히 로컬 서버로 요청을 보내다 실패하는 대신 눈에 띄게 알린다.
            if (serverBaseUrl.Contains("localhost"))
            {
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>("릴리즈 빌드인데 AuthServer serverBaseUrl이 기본값(localhost)입니다. 인스펙터에서 실제 서버 주소로 변경해야 합니다.");
            }
#endif
        }
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
        [Serializable] private class JwtPayloadBody { public long exp; }
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_username == MasterAccountUsername && _password == MasterAccountPassword)
            {
                ApplyMasterLogin();
                _onComplete?.Invoke(true, null);
                return;
            }
#endif
            _ = LoginAsync(_username, _password, _onComplete);
        }

        /// <summary>
        /// 저장된 RefreshToken으로 토큰을 재발급받는다(POST /api/auth/refresh, 토큰 로테이션 — RefreshToken도 함께 갱신됨).
        /// 인증이 필요한 요청(SendAuthorizedJsonRequestAsync)은 만료 시 알아서 재발급하므로 보통 직접 호출할 필요가 없다.
        /// </summary>
        public void Refresh(Action<bool, string> _onComplete = null)
        {
            _ = RefreshWithCallbackAsync(_onComplete);
        }

        /// <summary>
        /// 지금 쓸 수 있는 AccessToken을 돌려준다. 곧 만료되면 먼저 재발급한다. 로그인 상태가 아니거나 재발급에 실패하면 null.
        /// 401 재시도가 없는 경로(GameServer 입장 등)에서 토큰을 꺼낼 때 AccessToken 속성 대신 사용한다.
        /// </summary>
        public async Awaitable<string> GetValidAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(AccessToken))
            {
                return null;
            }

            if (AccessToken == proactiveRefreshAttemptedToken || !IsAccessTokenExpiringSoon(AccessToken))
            {
                return AccessToken;
            }

            proactiveRefreshAttemptedToken = AccessToken;
            bool refreshed = await RefreshSessionAsync(AccessToken);
            return refreshed ? AccessToken : null;
        }

        /// <summary>
        /// 서버에 로그아웃(RefreshToken revoke, POST /api/auth/logout)을 요청하고, 결과와 무관하게 로컬 세션은 정리한다.
        /// </summary>
        public void Logout(Action<bool> _onComplete = null)
        {
            _ = LogoutAsync(_onComplete);
        }
        /// <summary>
        /// 로그인 상태(AccessToken)가 필요한 API를 다른 매니저(예: SaveDataManager)가 호출할 때 사용하는 공용 헬퍼.
        /// AccessToken이 없으면(비로그인 상태) 요청을 보내지 않고 즉시 실패를 반환한다.
        /// AccessToken은 30분이면 만료된다. 만료가 임박하면 보내기 전에 재발급하고, 그래도 401이 오면(기기/서버 시계 차이 등)
        /// 한 번 재발급한 뒤 다시 보낸다 - 예전에는 재발급을 아무도 호출하지 않아, 로그인 30분 뒤부터 장비 장착/아이템 버리기 등
        /// 모든 요청이 401로 실패했다.
        /// </summary>
        public async Awaitable<(bool success, string body, string error)> SendAuthorizedJsonRequestAsync(string _path, string _method, string _jsonBody = null)
        {
            string accessToken = await GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                return (false, null, "로그인이 필요합니다.");
            }

            (bool success, string body, string error, long statusCode) = await SendJsonRequestAsync(_path, _method, _jsonBody, accessToken);
            if (statusCode != 401)
            {
                return (success, body, error);
            }

            if (!await RefreshSessionAsync(accessToken))
            {
                return (false, body, "로그인이 만료되었습니다. 다시 로그인해 주세요.");
            }

            (bool retrySuccess, string retryBody, string retryError, long _) = await SendJsonRequestAsync(_path, _method, _jsonBody, AccessToken);
            return (retrySuccess, retryBody, retryError);
        }

        #endregion

        #region Method - Internal
        private async Awaitable RegisterAsync(string _userName, string _password, string _nickname, Action<bool, string> _onComplete)
        {
            string json = JsonUtility.ToJson(new RegisterRequestBody { _userName = _userName, _password = _password, _nickname = _nickname });
            (bool success, string _, string error, long _) = await SendJsonRequestAsync("/api/users/register", "POST", json);

            _onComplete?.Invoke(success, error);
        }

        private async Awaitable LoginAsync(string _username, string _password, Action<bool, string> _onComplete)
        {
            string json = JsonUtility.ToJson(new LoginRequestBody { _username = _username, _password = _password });
            (bool success, string body, string error, long _) = await SendJsonRequestAsync("/api/auth/login", "POST", json);

            if (!success)
            {
                _onComplete?.Invoke(false, error);
                return;
            }

            ApplyLoginResponse(body);
            _onComplete?.Invoke(true, null);
        }

        private async Awaitable RefreshWithCallbackAsync(Action<bool, string> _onComplete)
        {
            bool refreshed = await RefreshSessionAsync();
            _onComplete?.Invoke(refreshed, refreshed ? null : "로그인이 만료되었습니다. 다시 로그인해 주세요.");
        }

        /// <summary>
        /// 재발급을 요청하되, 이미 진행 중이면 그 결과를 함께 기다린다(재발급 요청은 항상 하나). _staleAccessToken은 호출측이
        /// 만료됐다고 본 토큰으로, 그 사이 다른 요청이 이미 재발급해 AccessToken이 바뀌었다면 다시 보내지 않고 성공으로 본다.
        /// </summary>
        private Task<bool> RefreshSessionAsync(string _staleAccessToken = null)
        {
            if (_staleAccessToken != null && !string.IsNullOrEmpty(AccessToken) && AccessToken != _staleAccessToken)
            {
                return Task.FromResult(true);
            }

            if (refreshInFlight == null || refreshInFlight.IsCompleted)
            {
                refreshInFlight = RunRefreshAsync();
            }

            return refreshInFlight;
        }

        private async Task<bool> RunRefreshAsync()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 마스터 계정은 서버가 발급한 토큰이 아니라 재발급할 수 없다. 세션을 끊지도 않는다(서버 요청이 실패하는 건 원래 그렇다).
            if (RefreshToken == MasterDevRefreshToken)
            {
                return false;
            }
#endif
            if (string.IsNullOrEmpty(RefreshToken))
            {
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>("저장된 RefreshToken이 없어 재발급을 요청할 수 없습니다.");
                return false;
            }

            string json = JsonUtility.ToJson(new RefreshRequestBody { _refreshToken = RefreshToken });
            (bool success, string body, string error, long statusCode) = await SendJsonRequestAsync("/api/auth/refresh", "POST", json);

            if (success)
            {
                ApplyLoginResponse(body);
                return true;
            }

            // 서버가 RefreshToken을 거부했다(만료/폐기, 401) - 다시 로그인하는 수밖에 없다. 네트워크 오류나 서버 오류(5xx)로
            // 응답을 못 받은 경우는 세션을 유지한다 - 잠깐의 끊김으로 로그아웃시키지 않고, 다음 요청에서 다시 재발급을 시도한다.
            if (statusCode == 401)
            {
                DebugLogManager.GenerateErrorMessage<ServerConnectManager>($"RefreshToken이 만료되어 세션을 종료합니다 : {error}");
                ClearSession();
                OnSessionExpired?.Invoke();
            }

            return false;
        }

        // AccessToken(JWT)의 exp(만료 시각, 유닉스 초)를 읽어 AccessTokenRefreshMarginSeconds 안에 만료되는지 본다.
        // 서명은 검증하지 않는다(서버가 한다) - 언제 재발급할지 정하는 데만 쓴다. 읽을 수 없는 토큰이면 false(서버의 401에 맡긴다).
        private static bool IsAccessTokenExpiringSoon(string _accessToken)
        {
            string[] parts = _accessToken.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            try
            {
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

                JwtPayloadBody body = JsonUtility.FromJson<JwtPayloadBody>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                if (body == null || body.exp <= 0)
                {
                    return false;
                }

                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= body.exp - AccessTokenRefreshMarginSeconds;
            }
            catch (Exception)
            {
                return false;
            }
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
            (bool success, string _, string error, long _) = await SendJsonRequestAsync("/api/auth/logout", "POST", json);

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

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void ApplyMasterLogin()
        {
            AccessToken = "MASTER_DEV_ACCESS_TOKEN";
            RefreshToken = MasterDevRefreshToken;
            CurrentUser = new UserInfo
            {
                _id = -1,
                _username = MasterAccountUsername,
                _nickname = "Master",
                _createdAt = DateTime.UtcNow.ToString("o")
            };
            IsLoggedIn = true;
        }
#endif

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
        /// statusCode는 HTTP 상태 코드이며, 응답을 받지 못했으면(연결 실패/타임아웃) 0이다.
        /// </summary>
        private async Awaitable<(bool success, string body, string error, long statusCode)> SendJsonRequestAsync(string _path, string _method, string _jsonBody, string _accessToken = null)
        {
            string url = serverBaseUrl.TrimEnd('/') + _path;

            using UnityWebRequest request = new UnityWebRequest(url, _method);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(_jsonBody ?? string.Empty));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(_accessToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            }
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
                return (false, request.downloadHandler.text, errorMessage, request.responseCode);
            }

            return (true, request.downloadHandler.text, null, request.responseCode);
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
