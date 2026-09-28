namespace MainServer.AuthServer
{
    // [EnableRateLimiting]에 쓰는 정책 이름(정책 내용은 Program.cs의 AddRateLimiter).
    public static class RateLimitPolicies
    {
        // 로그인/회원가입/토큰 재발급/로그아웃 - 로그인 없이 호출할 수 있는 인증 API.
        public const string Auth = "auth";
    }
}
