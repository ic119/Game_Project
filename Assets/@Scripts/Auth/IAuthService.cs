using System;

public interface IAuthService
{
    /// 기기 고유 ID로 게스트 로그인 (없으면 자동 계정 생성)
    void LoginWithDeviceId(Action<AuthResult> onComplete);

    /// Google 로그인 (idToken은 Google Sign-In SDK에서 발급받은 값)
    void LoginWithGoogle(string idToken, Action<AuthResult> onComplete);

    /// 현재 게스트 계정에 Google 계정을 연동 (데이터 보존)
    void LinkGoogle(string idToken, Action<AuthResult> onComplete);

    /// 로그아웃 (로컬 세션 정리)
    void Logout();
}
